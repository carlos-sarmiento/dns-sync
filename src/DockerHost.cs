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

        public async Task<IList<ContainerDomainRecords>> GetContainersToAlias()
        {
            var containers = await this.client.Containers.ListContainersAsync(new ContainersListParameters());

            return containers.Select(c =>
                  {
                      var name = c.Names.First();
                      var syncLabels = c.Labels.Where(label => label.Key.StartsWith("dns-sync.")).ToList();

                      if (syncLabels.Count == 0)
                      {
                          return null;
                      }

                      var isEnabled = c.State == "running" && syncLabels.Any(label => label.Key == "dns-sync.enable" && label.Value == "true");
                      var mappingsStr = syncLabels.FirstOrDefault(label => label.Key == "dns-sync.domains").Value ?? "";

                      var parsedMappings = mappingsStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                      .Select(s => s.Trim())
                                                      .Distinct().ToList();

                      return new ContainerDomainRecords()
                      {
                          Uri = this.ConnectionUri.ToString(),
                          Hostname = this.Hostname,
                          UseAddressRecords = this.UseAddressRecords,
                          ContainerName = this.Hostname + name,
                          Domains = parsedMappings,
                          IsMappingEnabled = isEnabled
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
    }

    internal class ContainerDomainRecords
    {
        public ContainerDomainRecords()
        {
            Uri = "";
            Hostname = "";
            ContainerName = "";
            Domains = new List<string>();
        }
        public string Hostname { get; init; }
        public string Uri { get; init; }

        public string ContainerName { get; init; }
        public bool UseAddressRecords { get; init; }
        public bool IsMappingEnabled { get; init; }
        public IList<string> Domains { get; init; }
    }
}