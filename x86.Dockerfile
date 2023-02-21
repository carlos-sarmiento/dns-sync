FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY src/ ./
RUN dotnet restore

RUN dotnet publish -c Release -o out --self-contained false --no-restore

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:7.0-bullseye-slim
WORKDIR /app
COPY --from=build /app/out .
ENTRYPOINT ["dotnet", "dns-sync.dll"]
