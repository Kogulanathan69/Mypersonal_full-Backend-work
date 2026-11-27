FROM mcr.microsoft.com/dotnet/sdk:10.0

WORKDIR /src

COPY . .

RUN dotnet tool restore

RUN dotnet build src/BuildMind.API/BuildMind.API.csproj --configuration Release

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1

ENTRYPOINT ["dotnet", "tool", "run", "dotnet-ef"]
