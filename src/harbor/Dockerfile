# harbor — the gateway. The only thing exposed to the internet.
#
# Built from the repository root, because harbor references
# libs/observability for logging and the correlation id.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/harbor/MorganHacks.Harbor/MorganHacks.Harbor.csproj src/harbor/MorganHacks.Harbor/
COPY libs/observability/MorganHacks.Observability/MorganHacks.Observability.csproj \
     libs/observability/MorganHacks.Observability/
COPY libs/features/MorganHacks.Features/MorganHacks.Features.csproj \
     libs/features/MorganHacks.Features/
COPY global.json ./
COPY src/harbor/Directory.Build.props src/harbor/
COPY libs/Directory.Build.props       libs/
RUN dotnet restore src/harbor/MorganHacks.Harbor/MorganHacks.Harbor.csproj

COPY src/harbor/ src/harbor/
COPY libs/ libs/
RUN dotnet publish src/harbor/MorganHacks.Harbor/MorganHacks.Harbor.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Harbor installs nothing and still needs this. The base image and the
# distribution's fixes move on different schedules, so between them it carries
# packages Ubuntu has already patched — and harbor is the one service with a
# public address, which makes it the last place to run a known-vulnerable
# OpenSSL. The reasoning is written out in full in src/atlas/Dockerfile.
RUN apt-get update \
 && apt-get upgrade -y --no-install-recommends \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "MorganHacks.Harbor.dll"]
