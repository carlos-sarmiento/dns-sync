using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace dns_sync.plugins
{

    public class DnsmasqPlugin : DnsSyncPluginBase
    {
        public static string PluginName => "dnsmasq";

        public DnsmasqPlugin()
        {
            HostUri = "";
            TargetFile = "";
            ContainerName = "";
        }

        private string? HostUri { get; set; }
        private string? ContainerName { get; set; }
        private string TargetFile { get; set; }

        public override Task ConfigureAsync(Dictionary<string, object> rawConfig)
        {
            this.ConfigureBaseAsync<DnsmasqPlugin>(rawConfig);

            var hostUri = rawConfig.GetValueOrDefault("host_uri") as string;
            if (hostUri != null && string.IsNullOrWhiteSpace(hostUri))
            {
                throw new Exception($"Invalid URI provided for dnsmasq docker host: {rawConfig["host_uri"].ToString()}");
            }
            else
            {
                this.HostUri = hostUri;
            }

            var containerName = rawConfig.GetValueOrDefault("container_name") as string;
            if (containerName != null && string.IsNullOrWhiteSpace(containerName))
            {
                throw new Exception($"Invalid name provided for dnsmasq docker container: {rawConfig["container_name"]}");
            }
            else
            {
                this.ContainerName = containerName;
            }

            var targetFile = rawConfig.GetValueOrDefault("target_file") as string;
            if (string.IsNullOrWhiteSpace(targetFile))
            {
                throw new Exception($"Invalid target_file provided for dnsmasq plugin: {rawConfig["target_file"].ToString()}");
            }
            else
            {
                this.TargetFile = targetFile;
            }

            System.IO.File.AppendAllText(this.TargetFile, "");

            return Task.CompletedTask;
        }

        public override async Task ProcessContainersAsync(IList<ContainerRecord> containers)
        {
            var dnsmasqContent = GenerateDnsMasqFile(containers);
            var wasDnsmasqFileUpdated = UpdateDnsMasqFile(dnsmasqContent);
            await RestartDnsmasqInstance(wasDnsmasqFileUpdated);
        }

        private string GenerateDnsMasqFile(IList<ContainerRecord> recordsToCreate)
        {

            var dnsmasqFile = new StringBuilder();
            var duplicateDetection = new Dictionary<string, string>();

            var wwww = recordsToCreate.GroupBy(c => c.Hostname).ToDictionary(c => c.Key, c => c.ToArray());

            foreach (var recordsPerHost in wwww)
            {
                if (!recordsPerHost.Value.Any())
                {
                    continue;
                }

                var hostname = recordsPerHost.Key;


                foreach (var container in recordsPerHost.Value)
                {
                    if (container.IsActiveForDnsSync && container.GetLabelAsBool(new[] { "dnsmasq.register", "dns.register", "register_on_dns" }, defaultValue: true))
                    {

                        var domains = container.GetLabel(new[] { "dnsmasq.domains", "dns.domains", "domains" }) ?? "";

                        if (string.IsNullOrWhiteSpace(domains))
                        {
                            Logger.LogError($"{container.ContainerName} has no domains to register on DNS.");
                        }


                        var description = container.GetLabel("description") ?? domains;
                        var splitDomains = domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        dnsmasqFile.AppendLine($"# {container.ContainerName} -- {description}");

                        foreach (var domain in splitDomains)
                        {
                            if (duplicateDetection.ContainsKey(domain))
                            {
                                Logger.LogWarning($"{container.ContainerName} is trying to register '{domain}' which is already owned by Host: '{duplicateDetection[domain]}'");
                                continue;
                            }

                            duplicateDetection[domain] = container.ContainerName;
                            if (container.UseAddressRecords)
                            {
                                dnsmasqFile.AppendLine($"address=/{domain}/{hostname}");

                            }
                            else
                            {
                                if (domain != hostname)
                                {
                                    dnsmasqFile.AppendLine($"cname={domain},{hostname}");
                                }
                                else
                                {
                                    Logger.LogError($"Invalid CNAME config for container {container.ContainerName}. Domain and hostname are the same: {domain}");
                                }
                            }
                        }
                        dnsmasqFile.AppendLine("");
                    }
                    else
                    {
                        Logger.LogDebug($"{container.ContainerName} DNS generation is disabled");
                    }
                }
            }

            Logger.LogDebug("Processing Containers Done");

            return dnsmasqFile.ToString();

        }

        private async Task RestartDnsmasqInstance(bool wasDnsmasqFileUpdated)
        {
            var dnsmasqUri = this.HostUri;
            var dnsmasqContainerName = this.ContainerName;
            DockerHost? dnsmasqDockerHost = null;

            if (dnsmasqUri != null && dnsmasqContainerName != null)
            {
                var hostUri = new Uri(dnsmasqUri);
                var isMTLS = hostUri.Scheme == "https" || hostUri.Scheme == "unix";

                dnsmasqDockerHost = new DockerHost(hostUri, "", false, null);
            }

            if (wasDnsmasqFileUpdated && dnsmasqDockerHost != null && dnsmasqContainerName != null)
            {
                Logger.LogWarning("Reloading DNSMasq Files");
                await dnsmasqDockerHost.SendSignalToContainer(dnsmasqContainerName, "SIGHUP");
            }
        }

        private bool UpdateDnsMasqFile(string newDnsmasqContent)
        {

            var previousFile = System.IO.File.ReadAllText(this.TargetFile);

            if (newDnsmasqContent != previousFile)
            {
                Logger.LogWarning("DNSMasq File Changed");
                previousFile = newDnsmasqContent;

                System.IO.File.WriteAllText(this.TargetFile, newDnsmasqContent);
                return true;
            }
            else
            {
                Logger.LogDebug("No change to DNSMasq File");
                return false;
            }
        }

        public override string GetPluginName()
        {
            return PluginName;
        }
    }
}