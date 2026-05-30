---
name: docker
description: Containerize CritterBids (.NET 10, Marten/PostgreSQL, Wolverine) for local development and Hetzner VPS deployment. Use when writing or fixing a Dockerfile, docker-compose.yml, or .dockerignore for a CritterBids service; when the local container loop is misbehaving (stale image, container won't start, Postgres not reachable); or when deciding whether a change needs an image rebuild, a restart, or just a code reload. Covers multi-stage builds, non-root runtime, Marten/Postgres health gating, the local rebuild-vs-restart decision, and branch-swap hygiene.
license: MIT
compatibility: Requires Docker and the .NET 10 SDK.
metadata:
  author: CritterBids
  version: "0.1"
  status: draft-pending-accepted
  source: "Adapted from the docker skill in codewithmukesh/dotnet-claude-kit (MIT)"
---

# Docker — CritterBids

> Adapted from the MIT-licensed `docker` skill in codewithmukesh/dotnet-claude-kit, retargeted from EF Core to CritterBids' all-Marten + Wolverine modular monolith. CritterBids uses .NET Aspire (AppHost) for local orchestration and RabbitMQ for inter-BC messaging; this skill covers the Hetzner Compose deployment artifact and the rebuild/restart/reload decision loop for development.

## Local development: AppHost first, Compose for reference

For **local development**, use `dotnet run --project src/CritterBids.AppHost --launch-profile http`. The AppHost orchestrates PostgreSQL, RabbitMQ, and the API host, wiring them with health gates and providing a live dashboard at `http://localhost:15237`. This is the single local orchestration path.

The Compose examples below are for **reference and Hetzner VPS deployment**, not everyday development.

## Decision: rebuild, restart, or reload? (Compose ref only)

When you are running Compose locally or need to reason about image layering, use this:

| Change | Action |
|---|---|
| `.cs` code only, container has source mounted + `dotnet watch` | Nothing — hot reload picks it up |
| `.cs` code only, no watch / no mount | `docker compose restart <service>` |
| `.csproj`, `Directory.Build.props`, NuGet versions, new package | **Rebuild image** (`docker compose build <service>`) |
| `Dockerfile` or `.dockerignore` | **Rebuild image** |
| `appsettings*.json`, env vars, Compose env block | Recreate container: `docker compose up -d <service>` |
| Postgres schema / Marten registration change | Restart the app service (Marten applies schema on startup); rebuild only if code changed |

To confirm a change actually landed in a running container, check the log line you expect rather than assuming: `docker compose logs -f <service>`. If you don't see your new log statement, the image is stale — rebuild.

## Branch-swap hygiene

Switching branches/features changes registrations, schema, and sometimes the Compose topology. Before swapping:

1. `docker compose down` — stop the app services. Keep the Postgres volume unless the branches have incompatible schemas.
2. If the target branch has different Marten document/event types or projections, **also** drop the volume: `docker compose down -v`. Marten will rebuild schema on next startup. Skipping this leaves you debugging "phantom" schema from the other branch.
3. `docker compose up -d --build` on the new branch so the image matches the checked-out source.

## Dockerfile — the canonical shape

CritterBids deploys as a modular monolith: **`CritterBids.Api`** is the single deployable host that wires all eight BC modules. The Dockerfile is multi-stage: build in the SDK image, run in the slim ASP.NET runtime, run as non-root.

```dockerfile
# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first for layer caching — copy only project files
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/CritterBids.Api/CritterBids.Api.csproj", "src/CritterBids.Api/"]
COPY ["src/CritterBids.Contracts/CritterBids.Contracts.csproj", "src/CritterBids.Contracts/"]
COPY ["src/CritterBids.Participants/CritterBids.Participants.csproj", "src/CritterBids.Participants/"]
COPY ["src/CritterBids.Selling/CritterBids.Selling.csproj", "src/CritterBids.Selling/"]
COPY ["src/CritterBids.Listings/CritterBids.Listings.csproj", "src/CritterBids.Listings/"]
COPY ["src/CritterBids.Auctions/CritterBids.Auctions.csproj", "src/CritterBids.Auctions/"]
COPY ["src/CritterBids.Settlement/CritterBids.Settlement.csproj", "src/CritterBids.Settlement/"]
COPY ["src/CritterBids.Obligations/CritterBids.Obligations.csproj", "src/CritterBids.Obligations/"]
COPY ["src/CritterBids.Relay/CritterBids.Relay.csproj", "src/CritterBids.Relay/"]
RUN dotnet restore "src/CritterBids.Api/CritterBids.Api.csproj"

COPY . .
RUN dotnet publish "src/CritterBids.Api/CritterBids.Api.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# non-root
RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Health check hits an endpoint that verifies Postgres and RabbitMQ are reachable
HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=5 \
    CMD ["curl", "-f", "http://localhost:8080/health"] || exit 1

ENTRYPOINT ["dotnet", "CritterBids.Api.dll"]
```

Notes specific to this stack:
- **Health must gate on Postgres and RabbitMQ, not just the process.** All eight BC modules depend on Marten (PostgreSQL) and Wolverine messaging (RabbitMQ). Wire an ASP.NET health check endpoint (`/health`) that verifies both stores are reachable; the health check above uses `curl` to invoke it.
- **Wolverine codegen**: if you pre-generate Wolverine handler code (`WolverineOptions.CodeGeneration.TypeLoadMode`), generate during build so the runtime image doesn't JIT-compile handlers on first request. Otherwise the default dynamic mode is fine for a single-VPS deploy.
- **All eight BCs are present.** The monolith approach means each BC contributes its handlers, projections, and integrations via `AddXyzModule()` calls in `CritterBids.Api/Program.cs`. The Dockerfile copies all BC `.csproj` files and the restore step resolves all transitive dependencies.

## docker-compose — Hetzner VPS reference

Use this as a reference for on-VPS deployment. **Do not use this for local development — use AppHost instead.**

```yaml
version: '3.9'

services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_USER: critterbids
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: critterbids
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U critterbids"]
      interval: 5s
      timeout: 3s
      retries: 10
    restart: unless-stopped

  rabbitmq:
    image: rabbitmq:3.13-management-alpine
    environment:
      RABBITMQ_DEFAULT_USER: critterbids
      RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD}
    ports:
      - "15672:15672"  # management UI
    volumes:
      - rmqdata:/var/lib/rabbitmq
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
      interval: 5s
      timeout: 3s
      retries: 10
    restart: unless-stopped

  api:
    build:
      context: .
      dockerfile: Dockerfile
    depends_on:
      postgres:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy
    environment:
      ConnectionStrings__Marten: "Host=postgres;Database=critterbids;Username=critterbids;Password=${POSTGRES_PASSWORD}"
      ConnectionStrings__RabbitMQ: "amqp://critterbids:${RABBITMQ_PASSWORD}@rabbitmq:5672/"
      ASPNETCORE_HTTP_PORTS: "8080"
    ports:
      - "8080:8080"
    restart: unless-stopped

volumes:
  pgdata:
  rmqdata:
```

**Key points for VPS:**
- `depends_on: condition: service_healthy` ensures Postgres and RabbitMQ are ready before the API starts.
- Passwords are passed via env vars (loaded from a `.env` file on the VPS, never committed).
- Health checks gate service startup order.
- `restart: unless-stopped` ensures services auto-recover after a crash or reboot.
- Pin `postgres:17-alpine` and `rabbitmq:3.13-management-alpine` to match your local versions.

## .dockerignore

Keep build context small and avoid leaking local state into the image:

```
**/bin/
**/obj/
**/.vs/
**/node_modules/
.git/
.github/
docs/
*.user
**/appsettings.Development.json
```

## Hetzner deploy (Compose on the VPS)

This is local Compose with prod values, not a separate tool. The one thing to change: never bake secrets into the image. Pass the Marten connection string and any keys via an `.env` file on the VPS (referenced from Compose), kept out of git. Pin `postgres:17` to the exact tag running in prod so local reproduces prod.

## Anti-patterns

- Single-stage Dockerfile shipping the SDK to prod (huge image, build tools in runtime).
- Running as root.
- Rebuilding the image for a pure `.cs` change when watch/restart would do — wastes loop time.
- Sharing the Postgres volume across branches with incompatible Marten schema.
- Treating "process is up" as "service is ready" — gate health on the store.
