FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy local NuGet feed for Company.Observability (not on nuget.org)
COPY [".nuget-local/", ".nuget-local/"]
COPY ["nuget.config", "nuget.config"]

# Copy csproj files first for optimal layer caching
COPY ["src/Shared.Contracts/Shared.Contracts.csproj", "src/Shared.Contracts/"]
COPY ["src/LicenseCore.Application/LicenseCore.Application.csproj", "src/LicenseCore.Application/"]
COPY ["src/LicenseCore.API/LicenseCore.API.csproj", "src/LicenseCore.API/"]

RUN dotnet restore "src/LicenseCore.API/LicenseCore.API.csproj" \
    --configfile "nuget.config"

COPY . .

RUN dotnet publish "src/LicenseCore.API/LicenseCore.API.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LicenseCore.API.dll"]
