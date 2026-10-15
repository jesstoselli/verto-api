# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Restore-affecting files first so the NuGet layer only rebuilds when dependencies change.
COPY *.sln* Directory.Build.props ./
COPY src/Verto.Api/*.csproj            ./src/Verto.Api/
COPY src/Verto.Application/*.csproj    ./src/Verto.Application/
COPY src/Verto.Domain/*.csproj         ./src/Verto.Domain/
COPY src/Verto.Infrastructure/*.csproj ./src/Verto.Infrastructure/
COPY tests/Verto.Api.Tests/*.csproj    ./tests/Verto.Api.Tests/

RUN dotnet restore

COPY . .
RUN dotnet publish src/Verto.Api \
    -c Release \
    -o /out \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

RUN apk add --no-cache curl

USER app

COPY --from=build --chown=app:app /out ./

# nginx in front terminates TLS.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -fsS http://localhost:8080/v1/health || exit 1

ENTRYPOINT ["dotnet", "Verto.Api.dll"]
