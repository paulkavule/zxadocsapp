# syntax=docker/dockerfile:1
#
# Blazor Server UI. Build from THIS repo's root:
#   docker compose up --build
#
#   docker build -f zxadocsui/Dockerfile --secret id=ghtoken,env=GH_PACKAGES_TOKEN -t zxadocs-ui .
#
# No node stage: the Tailwind output is committed and the csproj has no npm step.
#
# --------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .

# GitHub Packages refuses anonymous restore. nuget.config already reads
# %GH_PACKAGES_TOKEN%; a secret mount is not a layer, unlike ARG or ENV.
RUN --mount=type=secret,id=ghtoken \
    GH_PACKAGES_TOKEN="$(cat /run/secrets/ghtoken)" \
    dotnet restore zxadocsui/zxadocsui.csproj

RUN dotnet publish zxadocsui/zxadocsui.csproj -c Release -o /app --no-restore

# --------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .

# Configuration comes from compose env_file; base image already sets port 8080.
# Reached only through nginx, which terminates TLS — see UseForwardedHeaders in Program.cs.
EXPOSE 8080
ENTRYPOINT ["dotnet", "zxadocsui.dll"]
