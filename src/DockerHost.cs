using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using Docker.DotNet.X509;

namespace dns_sync
{
    internal class DockerHost
    {
        public DockerHost(Uri hostUri, string? hostnameOverride, bool useAddressRecords, CertificateCredentials? credentials)
        {
            ConnectionUri = hostUri;
            Hostname = hostnameOverride ?? hostUri.Host;
            UseAddressRecords = useAddressRecords;

            var config = new DockerClientConfiguration(hostUri, credentials, TimeSpan.FromSeconds(10));
            this.client = config.CreateClient();
        }

        private DockerClient client { get; set; }

        public string Hostname { get; private set; }
        public bool UseAddressRecords { get; private set; }

        public Uri ConnectionUri { get; private set; }

        public async Task<IList<ContainerRecord>> GetContainersToAlias()
        {
            var containers = await this.client.Containers.ListContainersAsync(new ContainersListParameters());

            return containers.Select(c =>
                  {
                      var name = c.Names.First();
                      var syncLabels = c.Labels.Where(label => label.Key.StartsWith("dns-sync.")).ToDictionary(a => a.Key, a => a.Value);

                      if (syncLabels.Count == 0)
                      {
                          return null;
                      }

                      var isEnabled = c.State == "running" && syncLabels.Any(label => label.Key == "dns-sync.enable" && label.Value == "true");
                      var showInDashboard = !syncLabels.Any(label => label.Key == "dns-sync.show_in_dashboard") || syncLabels.Any(label => label.Key == "dns-sync.show_in_dashboard" && label.Value == "true");
                      var registerOnDns = !syncLabels.Any(label => label.Key == "dns-sync.register_on_dns") || syncLabels.Any(label => label.Key == "dns-sync.register_on_dns" && label.Value == "true");
                      var mappingsStr = syncLabels.FirstOrDefault(label => label.Key == "dns-sync.domains").Value ?? "";
                      var description = syncLabels.FirstOrDefault(label => label.Key == "dns-sync.description").Value ?? mappingsStr;
                      var category = syncLabels.FirstOrDefault(label => label.Key == "dns-sync.category").Value ?? this.Hostname;
                      var service = syncLabels.FirstOrDefault(label => label.Key == "dns-sync.service_name").Value;

                      var parsedMappings = mappingsStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                      .Select(s => s.Trim())
                                                      .Distinct().ToList();

                      return new ContainerRecord()
                      {
                          Uri = this.ConnectionUri.ToString(),
                          Hostname = this.Hostname,
                          UseAddressRecords = this.UseAddressRecords,
                          ContainerName = this.Hostname + name,
                          Description = description,
                          Domains = parsedMappings,
                          IsMappingEnabled = isEnabled,
                          Category = category,
                          ShowInDashboard = showInDashboard,
                          ServiceName = service,
                          RegisterOnDns = registerOnDns,
                      };
                  }).WhereNotNull().ToList();
        }

        public async Task RestartContainer(string containerName)
        {
            var containers = await this.client.Containers.ListContainersAsync(new ContainersListParameters());
            var containerId = containers.Where(c => c.Names.Contains($"/{containerName}")).Select(c =>
                               c.ID).FirstOrDefault();

            await this.client.Containers.RestartContainerAsync(containerId, new ContainerRestartParameters());
        }

        public async Task SendSignalToContainer(string containerName, string signal)
        {
            var containers = await this.client.Containers.ListContainersAsync(new ContainersListParameters());
            var containerId = containers.Where(c => c.Names.Contains($"/{containerName}")).Select(c =>
                               c.ID).FirstOrDefault();

            await this.client.Containers.KillContainerAsync(containerId, new ContainerKillParameters() { Signal = signal });
        }
    }

    internal class ContainerRecord
    {
        public ContainerRecord()
        {
            Uri = "";
            Hostname = "";
            ContainerName = "";
            Domains = new List<string>();
            Description = "";
            Category = "";
            ServiceName = "";
            Labels = new Dictionary<string, string>();
        }
        public string Hostname { get; init; }
        public string Uri { get; init; }
        public string ContainerName { get; init; }
        public string Category { get; init; }
        public bool UseAddressRecords { get; init; }
        public bool IsMappingEnabled { get; init; }
        public bool ShowInDashboard { get; init; }
        public bool RegisterOnDns { get; init; }
        public string? ServiceName { get; init; }
        public IList<string> Domains { get; init; }
        public IDictionary<string, string> Labels { get; init; }
        public string Description { get; init; }
    }
}