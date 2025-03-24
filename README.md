# DNS-SYNC for DNSMasq

docker buildx build -t dns-sync:latest .

## To run in local machine

dotnet build && dotnet run --project src/dns-sync.csproj ./dns-sync-config.yml
