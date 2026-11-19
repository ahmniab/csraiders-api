# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json ./

# Restore first so source edits do not invalidate the NuGet layer.
COPY src/CsRaiders.Api.Domain/CsRaiders.Api.Domain.csproj src/CsRaiders.Api.Domain/
COPY src/CsRaiders.Api.Application/CsRaiders.Api.Application.csproj src/CsRaiders.Api.Application/
COPY src/CsRaiders.Api.Infrastructure/CsRaiders.Api.Infrastructure.csproj src/CsRaiders.Api.Infrastructure/
COPY src/CsRaiders.Api.API/CsRaiders.Api.API.csproj src/CsRaiders.Api.API/
COPY tools/CsRaiders.Api.BenchSeeder/CsRaiders.Api.BenchSeeder.csproj tools/CsRaiders.Api.BenchSeeder/

RUN dotnet restore src/CsRaiders.Api.API/CsRaiders.Api.API.csproj \
 && dotnet restore tools/CsRaiders.Api.BenchSeeder/CsRaiders.Api.BenchSeeder.csproj

COPY src/ src/
COPY tools/ tools/

RUN dotnet publish src/CsRaiders.Api.API/CsRaiders.Api.API.csproj \
    -c Release -o /out/api /p:UseAppHost=false

RUN dotnet publish tools/CsRaiders.Api.BenchSeeder/CsRaiders.Api.BenchSeeder.csproj \
    -c Release -o /out/seed /p:UseAppHost=false


# One-shot migration + data seeding image (run before the API starts).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS seed
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out/seed ./
ENTRYPOINT ["dotnet", "CsRaiders.Api.BenchSeeder.dll"]


# API runtime image (default target).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out/api ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "CsRaiders.Api.API.dll"]
