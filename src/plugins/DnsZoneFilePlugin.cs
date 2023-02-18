using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace dns_sync.plugins
{

    public class DnsZoneFilePlugin : DnsSyncPluginBase
    {
        public static string PluginName => "dns_zone_file";

        public DnsZoneFilePlugin()
        {
            TargetFile = "";
        }

        private string TargetFile { get; set; }

        public override Task ConfigureAsync(Dictionary<string, object> rawConfig)
        {
            this.ConfigureBaseAsync<DnsZoneFilePlugin>(rawConfig);

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

        public override Task ProcessContainersAsync(IList<ContainerRecord> containers)
        {
            var dnsmasqContent = GenerateFile(containers);
            var wasDnsmasqFileUpdated = UpdateFile(dnsmasqContent);

            return Task.CompletedTask;
        }

        private string GenerateFile(IList<ContainerRecord> recordsToCreate)
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

                        dnsmasqFile.AppendLine($"; {container.ContainerName} -- {description}");

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
                                dnsmasqFile.AppendLine($"{domain}. IN  A {hostname}");

                            }
                            else
                            {
                                if (domain != hostname)
                                {
                                    dnsmasqFile.AppendLine($"{domain}. IN  CNAME {hostname}");
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

        private bool UpdateFile(string newDnsmasqContent)
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