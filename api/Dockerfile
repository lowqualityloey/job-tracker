FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the Central Package Management input BEFORE restore, and only then the project file.
#
# `api/Directory.Packages.props` sets `ManagePackageVersionsCentrally=true` and carries every `<PackageVersion>`,
# so the `.csproj` files declare `<PackageReference>` with no `Version` attribute. NuGet resolves those versions
# by walking up from the project to find the props file; when the restore stage contains only the `.csproj`, the
# walk finds nothing and every unpinned reference is an error. Measured twice on 2026-09-13 with a clean context
# (`docker build -f api/Dockerfile api`), failing identically in 4s:
#
#   error NU1015: The following PackageReference item(s) do not have a version specified:
#   Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Design,
#   Microsoft.EntityFrameworkCore.Relational, Npgsql.EntityFrameworkCore.PostgreSQL
#
# This layer is first because it changes rarely: editing application code invalidates `COPY . .` below but not
# the restore, so the dependency graph stays cached.
COPY ["Directory.Packages.props", "./"]
COPY ["src/JobTracker.Api/JobTracker.Api.csproj", "src/JobTracker.Api/"]
RUN dotnet restore "src/JobTracker.Api/JobTracker.Api.csproj"

# Copy everything and build
COPY . .
RUN dotnet publish "src/JobTracker.Api/JobTracker.Api.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The runtime stage makes no network calls. `apt-get install curl` used to live here to support a
# `HEALTHCHECK`; it was the only layer in the build that needed a package mirror, and the only one that
# did not complete in this sandbox (two attempts, both stalled inside `apt-get`, while every .NET layer
# finished). Health probing moved where it is actually enforced — see the note by `EXPOSE`.
COPY --from=build /app/publish .

# The port this container listens on is DECLARED here and nowhere else.
#
# `mcr.microsoft.com/dotnet/aspnet:10.0` already sets `ASPNETCORE_HTTP_PORTS=8080` (measured:
# `docker run --rm mcr.microsoft.com/dotnet/aspnet:10.0 env`), which is why the previous `EXPOSE 5000` was a
# comment rather than a contract — Kestrel was listening on 8080 while the image advertised 5000. Setting it
# explicitly means the value an ALB target group must be pointed at is written in the file that builds the
# image, not inherited from a base image that can change underneath us.
ENV ASPNETCORE_HTTP_PORTS=8080

# One declared port, and no `HEALTHCHECK` instruction.
#
# `/api/health` is real — `Program.cs:203-209` maps `GET /api/health` to `200 {"status":"ok","database":"reachable"}`
# when `CanConnectAsync` succeeds, and a bare `503` otherwise. It sits outside `SessionGate.IsProtected`, so it answers
# with no session cookie. What was wrong was the *port contract*: the image advertised `EXPOSE 5000` while
# Kestrel listens on 8080 (`ASPNETCORE_HTTP_PORTS=8080` is baked into `mcr.microsoft.com/dotnet/aspnet:10.0`;
# measured with `docker run --rm --entrypoint env mcr.microsoft.com/dotnet/aspnet:10.0`), so the old health
# check probed a port nothing was bound to and reported `unhealthy` forever.
#
# The check is now performed from outside the container — by the ALB target group in production and by `curl`
# on the host in a smoke test — instead of by a `curl` installed inside it. An in-image check would also need
# the task definition's own `healthCheck` to be set before ECS acted on it, so it bought no behaviour that the
# target-group check does not already enforce.
EXPOSE 8080

# Run as the unprivileged `app` user the base image ships rather than as root.
#
# Measured on `mcr.microsoft.com/dotnet/aspnet:10.0`: the default identity is `uid=0(root)` with `HOME=/root`,
# and the image defines `APP_UID=1654` plus a matching `app:x:1654:1654::/home/app:/bin/sh`. `COPY` above
# leaves the published bytes root-owned and world-readable, so the process can serve them but cannot rewrite
# its own binaries. Nothing needs to be writable: the Data Protection key ring that encrypts session cookies
# lives in the database (`Auth/PostgresKeyRing.cs` → the `data_protection_keys` table, wired at
# `Program.cs:58-62`), not in `$HOME/.aspnet`, and logging goes to stdout — which is what makes a non-root
# runtime here a free change rather than a volume-permission exercise.
USER $APP_UID

ENTRYPOINT ["dotnet", "JobTracker.Api.dll"]

