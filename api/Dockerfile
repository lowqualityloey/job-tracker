FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY ["src/JobTracker.Api/JobTracker.Api.csproj", "src/JobTracker.Api/"]
RUN dotnet restore "src/JobTracker.Api/JobTracker.Api.csproj"

# Copy everything and build
COPY . .
RUN dotnet publish "src/JobTracker.Api/JobTracker.Api.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:5000/api/health || exit 1

EXPOSE 5000

ENTRYPOINT ["dotnet", "JobTracker.Api.dll"]
