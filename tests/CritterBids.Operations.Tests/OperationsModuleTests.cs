using CritterBids.Operations.Tests.Fixtures;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace CritterBids.Operations.Tests;

[Collection(OperationsTestCollection.Name)]
public class OperationsModuleTests
{
    private readonly OperationsTestFixture _fixture;

    public OperationsModuleTests(OperationsTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void OperationsModule_BootsClean()
    {
        // Verify the test host started without throwing — AlbaHost construction succeeded.
        // This proves AddOperationsModule() registered cleanly alongside the primary Marten store
        // and that Program.cs's Wolverine assembly discovery for the Operations assembly (which
        // includes SettlementQueueHandler) did not surface a code-gen failure. It also indirectly
        // verifies the six cross-BC discovery exclusions (Selling / Auctions / Listings /
        // Settlement / Obligations / Relay) correctly suppress foreign-BC handlers without breaking
        // host startup.
        _fixture.Host.ShouldNotBeNull();

        // Verify the primary IDocumentStore is resolvable from the DI container. Per ADR 009/011,
        // all Marten BCs share a single IDocumentStore registered in Program.cs — Operations
        // contributes SettlementQueueView to it via services.ConfigureMarten().
        var store = _fixture.Host.Services.GetRequiredService<IDocumentStore>();
        store.ShouldNotBeNull();
    }

    [Fact]
    public async Task SettlementQueueView_IsMappedTo_OperationsSchema()
    {
        // The module wires opts.Schema.For<SettlementQueueView>().DatabaseSchemaName("operations").
        // A silent regression to the default "public" schema would still pass the behavior tests
        // (store/load works in any schema), so assert the physical table location directly. Apply
        // schema changes so the table exists, then query information_schema definitively.
        var store = _fixture.Host.Services.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var session = _fixture.GetDocumentSession();
        var schema = await session.QueryAsync<string>(
            "select table_schema from information_schema.tables where table_name = ?",
            "mt_doc_settlementqueueview");

        schema.ShouldContain("operations");
    }

    [Fact]
    public async Task LotBoardView_AndBidActivityEntry_AreMappedTo_OperationsSchema()
    {
        // The M7-S3 additions wire both new document types into the operations schema via
        // opts.Schema.For<…>().DatabaseSchemaName("operations"). A silent regression to the default
        // "public" schema would still pass the behavior tests, so assert the physical table
        // locations directly against information_schema.
        var store = _fixture.Host.Services.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var session = _fixture.GetDocumentSession();

        var lotBoardSchema = await session.QueryAsync<string>(
            "select table_schema from information_schema.tables where table_name = ?",
            "mt_doc_lotboardview");
        lotBoardSchema.ShouldContain("operations");

        var bidActivitySchema = await session.QueryAsync<string>(
            "select table_schema from information_schema.tables where table_name = ?",
            "mt_doc_bidactivityentry");
        bidActivitySchema.ShouldContain("operations");
    }

    [Fact]
    public async Task OperationsObligationsView_IsMappedTo_OperationsSchema()
    {
        // The M7-S4 addition wires OperationsObligationsView into the operations schema via
        // opts.Schema.For<…>().DatabaseSchemaName("operations"). A silent regression to the default
        // "public" schema would still pass the behavior tests, so assert the physical table
        // location directly against information_schema (the S2 learning that behaviour tests are
        // schema-blind).
        var store = _fixture.Host.Services.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var session = _fixture.GetDocumentSession();
        var schema = await session.QueryAsync<string>(
            "select table_schema from information_schema.tables where table_name = ?",
            "mt_doc_operationsobligationsview");

        schema.ShouldContain("operations");
    }
}
