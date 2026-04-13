using CritterBids.Participants;
using Polecat;
using Wolverine;
using Wolverine.Polecat;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

builder.UseWolverine(opts =>
{
    // RabbitMQ transport — connection string injected by Aspire via WithReference(rabbitMq).
    // Format: amqp://username:password@host:port
    var rabbitMqUri = builder.Configuration.GetConnectionString("rabbitmq")
        ?? throw new InvalidOperationException("RabbitMQ connection string not found. Run via CritterBids.AppHost.");
    opts.UseRabbitMq(new Uri(rabbitMqUri));

    // Wrap all Wolverine message handlers in Polecat transactions automatically.
    // Established here at host level; applies to all BCs registered via AddXyzModule().
    opts.Policies.AutoApplyTransactions();
});

var sqlServerConnectionString = builder.Configuration.GetConnectionString("sqlserver")
    ?? throw new InvalidOperationException("SQL Server connection string not found. Run via CritterBids.AppHost.");

builder.Services.AddPolecat(opts =>
{
    // SQL Server — connection string injected by Aspire via WithReference(sqlServer).
    // No schema name set here — each BC sets its own schema via ConfigurePolecat() in AddXyzModule().
    opts.ConnectionString = sqlServerConnectionString;
})
.IntegrateWithWolverine();

// Participants BC — configures Polecat schema "participants" and stream identity via ConfigurePolecat().
builder.Services.AddParticipantsModule(sqlServerConnectionString);

var app = builder.Build();
app.Run();
