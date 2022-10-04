# syntax=docker/dockerfile:1
#FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-env
FROM mcr.microsoft.com/dotnet/sdk:6.0-bullseye-slim-arm32v7 AS build-env
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY src/ ./
RUN dotnet restore
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:6.0-alpine
WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "dns-sync.dll"]