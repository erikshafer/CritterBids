---
name: docker
description: Containerize CritterBids (.NET 10, Marten/PostgreSQL, Wolverine) for local development and Hetzner VPS deployment. Use when writing or fixing a Dockerfile, docker-compose.yml, or .dockerignore for a CritterBids service; when the local container loop is misbehaving (stale image, container won't start, Postgres not reachable); or when deciding whether a change needs an image rebuild, a restart, or just a code reload. Covers multi-stage builds, non-root runtime, Marten/Postgres health gating, the local rebuild-vs-restart decision, and branch-swap hygiene.
license: MIT
compatibility: Requires Docker and the .NET 10 SDK.
metadata:
  author: CritterBids
  version: "0.1"
  status: draft-pending-review
  source: "Adapted from the docker skill in codewithmukesh/dotnet-claude-kit (MIT)"
---

# Docker — CritterBids

> Adapted from the MIT-licensed `docker` skill in codewithmukesh/dotnet-claude-kit, retargeted from EF Core / Aspire to CritterBids' all-Marten + Wolverine stack and Hetzner Compose deployment. Note: CritterMart uses .NET Aspire for orchestration; CritterBids does not — it deploys as Compose on a VPS, so this skill stays Compose-first.

## Decision: rebuild, restart, or reload?

Most local-loop friction comes from rebuilding when you didn't need to, or *not* rebuilding when you did. Use this:

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

Multi-stage, always. Build in the SDK image, run in the slim ASP.NET runtime image, run as non-root.

```dockerfile
# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first for layer caching — copy only project files
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/CritterBids.Auctions/CritterBids.Auctions.csproj", "src/CritterBids.Auctions/"]
# (repeat COPY for the other BC projects this service references)
RUN dotnet restore "src/CritterBids.Auctions/CritterBids.Auctions.csproj"

COPY . .
RUN dotnet publish "src/CritterBids.Auctions/CritterBids.Auctions.csproj" \
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

# Health check hits an endpoint that verifies the Marten store is reachable
HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=5 \
    CMD ["dotnet", "CritterBids.Auctions.dll", "--healthcheck"] || exit 1

ENTRYPOINT ["dotnet", "CritterBids.Auctions.dll"]
```

Notes specific to this stack:
- **Health must gate on Postgres, not just the process.** Wolverine and Marten both need the store reachable before the service is "ready." Wire an ASP.NET health check that opens a Marten session (or pings Postgres) and expose it at `/health`; prefer that HTTP probe over the CLI form above if the service is a web host.
- **Wolverine codegen**: if you pre-generate Wolverine handler code (`WolverineOptions.CodeGeneration.TypeLoadMode`), generate during build so the runtime image doesn't JIT-compile handlers on first request. Otherwise the default dynamic mode is fine for a single-VPS deploy.

## docker-compose — local dev

```yaml
services:
  postgres:
    image: postgres:17
    environment:
      POSTGRES_USER: critterbids
      POSTGRES_PASSWORD: critterbids
      POSTGRES_DB: critterbids
    ports: ["5432:5432"]
    volumes: ["pgdata:/var/lib/postgresql/data"]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U critterbids"]
      interval: 5s
      timeout: 3s
      retries: 10

  auctions:
    build:
      context: .
      dockerfile: src/CritterBids.Auctions/Dockerfile
    depends_on:
      postgres:
        condition: service_healthy   # don't start the app until PG is ready
    environment:
      ConnectionStrings__Marten: "Host=postgres;Database=critterbids;Username=critterbids;Password=critterbids"
    ports: ["8080:8080"]

volumes:
  pgdata:
```

`depends_on: condition: service_healthy` is the bit people skip — without it the app races Postgres and crashes on first boot.

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
