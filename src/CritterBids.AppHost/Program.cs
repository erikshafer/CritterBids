var builder = DistributedApplication.CreateBuilder(args);

// All infrastructure containers are labelled with com.docker.compose.project=critterbids
// so Docker Desktop groups them under the "critterbids" stack in its Containers view.
const string dockerProject = "critterbids";

// PostgreSQL 18 — for Marten BCs arriving in M2+; not consumed in M1 but wired now
// so M2 requires no infrastructure changes. Uses postgres:18-alpine for a small image.
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18-alpine")
    .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={dockerProject}");

// SQL Server 2022 — for Polecat BCs (Participants in M1, Settlement and Operations later).
// Uses mcr.microsoft.com/mssql/server:2022-latest (Microsoft's official image).
var sqlServer = builder.AddSqlServer("sqlserver")
    .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={dockerProject}");

// RabbitMQ 3 management image — includes the management UI, useful for the Aspire demo
// dashboard and local debugging. No active subscribers in M1; SellerRegistrationCompleted
// will be published into it starting in M1-S6.
var rabbitMq = builder.AddRabbitMQ("rabbitmq")
    .WithImageTag("3-management")
    .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={dockerProject}");

builder.AddProject<Projects.CritterBids_Api>("critterbids-api")
    .WithReference(postgres)
    .WithReference(sqlServer)
    .WithReference(rabbitMq)
    .WaitFor(postgres)
    .WaitFor(sqlServer)
    .WaitFor(rabbitMq);

builder.Build().Run();
