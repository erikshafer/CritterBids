using System.Text.Json;
using CritterBids.Api;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CritterBids.Api.Tests;

/// <summary>
/// Unit tests for <see cref="ConcurrencyConflictMiddleware"/> (the M8-S3a deferred item: a
/// commit-time concurrency loser on an HTTP request must surface as a graceful 409, not a 500).
/// Pure-middleware tests — no host, no database: the DCB consistency check that produces
/// <see cref="DcbConcurrencyException"/> in production is exercised by the existing
/// PlaceBidEndpointTests concurrency test; here we only assert the HTTP mapping. Note
/// <see cref="DcbConcurrencyException"/> derives from <see cref="ConcurrencyException"/>
/// (JasperFx.Events 2.8.2), so one catch covers the DCB check, per-stream appends, and saga
/// revision conflicts.
/// </summary>
public class ConcurrencyConflictMiddlewareTests
{
    private static DefaultHttpContext BuildContext()
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static ConcurrencyConflictMiddleware BuildMiddleware(RequestDelegate next) =>
        new(next, NullLogger<ConcurrencyConflictMiddleware>.Instance);

    private static async Task<JsonElement> ReadProblemAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var raw = await reader.ReadToEndAsync();
        return JsonDocument.Parse(raw).RootElement;
    }

    [Fact]
    public async Task DcbConcurrencyException_maps_to_409_problem_details()
    {
        var middleware = BuildMiddleware(_ =>
            throw new DcbConcurrencyException(new EventTagQuery(), lastSeenSequence: 42));
        var context = BuildContext();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        var problem = await ReadProblemAsync(context);
        problem.GetProperty("reason").GetString().ShouldBe("ConcurrencyConflict");
        problem.GetProperty("title").GetString().ShouldBe("Concurrency conflict");
    }

    [Fact]
    public async Task plain_ConcurrencyException_maps_to_409()
    {
        var middleware = BuildMiddleware(_ =>
            throw new ConcurrencyException("saga revision conflict", typeof(object), Guid.Empty));
        var context = BuildContext();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        var problem = await ReadProblemAsync(context);
        problem.GetProperty("reason").GetString().ShouldBe("ConcurrencyConflict");
    }

    [Fact]
    public async Task unrelated_exceptions_propagate_unchanged()
    {
        var middleware = BuildMiddleware(_ => throw new InvalidOperationException("boom"));
        var context = BuildContext();

        await Should.ThrowAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
    }

    [Fact]
    public async Task successful_requests_pass_through_untouched()
    {
        var middleware = BuildMiddleware(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        var context = BuildContext();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task conflict_after_response_started_rethrows()
    {
        var middleware = BuildMiddleware(_ =>
            throw new ConcurrencyException("late conflict", typeof(object), Guid.Empty));
        var context = BuildContext();
        // DefaultHttpContext has no real server, so Response.StartAsync() never flips HasStarted —
        // install a feature that reports the response as started instead.
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(
            new StartedResponseFeature());

        await Should.ThrowAsync<ConcurrencyException>(() => middleware.InvokeAsync(context));
    }

    private sealed class StartedResponseFeature : Microsoft.AspNetCore.Http.Features.HttpResponseFeature
    {
        public override bool HasStarted => true;
    }
}
