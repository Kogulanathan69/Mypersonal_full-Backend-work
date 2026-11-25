# BuildMind API (staging/production-oriented build; NOT deployed). Multi-stage, non-root, no secrets baked in.
# Build context: repository root.  docker build -f deploy/captain/api.Dockerfile -t buildmind-api:local .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/BuildMind.API/BuildMind.API.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .
# The aspnet image ships the non-root "app" user (UID 1654).
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=0
EXPOSE 8080
# No in-image healthcheck (the aspnet image has no curl); the orchestrator probes GET /health/ready.
ENTRYPOINT ["dotnet", "BuildMind.API.dll"]
