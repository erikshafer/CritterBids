var builder = DistributedApplication.CreateBuilder(args);

// All infrastructure containers are labelled with com.docker.compose.project=critterbids
// so Docker Desktop groups them under the "critterbids" stack in its Containers view.
const string dockerProject = "critterbids";

// PostgreSQL 18 — shared by all eight BCs (ADR 011: All-Marten pivot).
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18-alpine")
    .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={dockerProject}");

// SQL Server removed — Participants BC migrated to Marten (ADR 011 Participants migration session).
// WolverineFx.Http.Polecat + Polecat packages archived; see docs/decisions/011-all-marten-pivot.md.

// RabbitMQ 3 management image — includes the management UI, useful for the Aspire demo
// dashboard and local debugging. WithManagementPlugin publishes the 15672 UI endpoint to
// the Aspire dashboard so local smoke-tests and workshop demos can inspect queues without
// docker exec (M3-S7 operational-smoke gap; closed at M4-S1).
var rabbitMq = builder.AddRabbitMQ("rabbitmq")
    .WithImageTag("3-management")
    .WithManagementPlugin()
    .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={dockerProject}");

builder.AddProject<Projects.CritterBids_Api>("critterbids-api")
    .WithReference(postgres)
    .WithReference(rabbitMq)
    .WaitFor(postgres)
    .WaitFor(rabbitMq);

builder.Build().Run();
