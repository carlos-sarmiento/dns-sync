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

            var prefix = "dns-sync.";

            return containers.Select(c =>
                  {
                      var name = c.Names.First();
                      var syncLabels = c.Labels.Where(label => label.Key.StartsWith(prefix)).ToDictionary(a => a.Key, a => a.Value);

                      if (syncLabels.Count == 0)
                      {
                          return null;
                      }

                      var isEnabled = c.State == "running" && syncLabels.Any(label => label.Key == $"{prefix}enable" && label.Value == "true");
                      var service = syncLabels.FirstOrDefault(label => label.Key == $"{prefix}service_name").Value;

                      var labels = syncLabels.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key.Remove(0, prefix.Length), kv => kv.Value);

                      return new ContainerRecord(labels)
                      {
                          Uri = this.ConnectionUri.ToString(),
                          Hostname = this.Hostname,
                          ContainerName = this.Hostname + name,
                          IsActiveForDnsSync = isEnabled,
                          ServiceName = service ?? name,
                          UseAddressRecords = this.UseAddressRecords,
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

    public class ContainerRecord
    {
        public ContainerRecord(IReadOnlyDictionary<string, string> labels)
        {
            Uri = "";
            Hostname = "";
            ContainerName = "";
            ServiceName = "";
            Labels = labels;
        }
        public string Hostname { get; init; }
        public string Uri { get; init; }
        public string ContainerName { get; init; }
        public IReadOnlyDictionary<string, string> Labels { get; init; }
        public bool IsActiveForDnsSync { get; init; }
        public string ServiceName { get; init; }
        public bool UseAddressRecords { get; init; }
        public bool GetLabelAsBool(string label, bool defaultValue = false)
        {
            if (Boolean.TryParse(Labels.GetValueOrDefault(label), out var val))
            {
                return val;
            }
            else
            {
                return defaultValue;
            }
        }

        public bool GetLabelAsBool(string[] possibleLabels, bool defaultValue = false)
        {
            foreach (var label in possibleLabels)
            {
                if (Boolean.TryParse(Labels.GetValueOrDefault(label), out var val))
                {
                    return val;
                }
            }

            return defaultValue;
        }

        public string? GetLabel(string label)
        {
            if (Labels.ContainsKey(label))
            {
                return Labels[label];
            }

            return null;
        }

        public string? GetLabel(string[] possibleLabels)
        {
            foreach (var label in possibleLabels)
            {
                if (Labels.ContainsKey(label))
                {
                    return Labels[label];
                }
            }

            return null;
        }
    }
}