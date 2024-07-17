using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace dns_sync.plugins
{

    public class DnsZoneFilePlugin : DnsSyncPluginBase
    {
        public static string PluginName => "dns_zone_file";

        private string TargetFile { get; set; }
        private bool AllowDuplicates { get; set; }

        public DnsZoneFilePlugin()
        {
            TargetFile = "";
        }

        public override Task ConfigureAsync(Dictionary<string, object> rawConfig)
        {
            this.ConfigureBaseAsync<DnsZoneFilePlugin>(rawConfig);

            var targetFile = rawConfig.GetValueOrDefault("target_file") as string;
            if (string.IsNullOrWhiteSpace(targetFile))
            {
                throw new Exception($"Invalid target_file provided for dns_zone_file plugin: {rawConfig["target_file"].ToString()}");
            }
            else
            {
                this.TargetFile = targetFile;
            }

            var allowDuplicates = bool.TryParse(rawConfig.GetValueOrDefault("allow_duplicates", "false").ToString(), out var logQueries);
            this.AllowDuplicates = allowDuplicates;

            System.IO.File.AppendAllText(this.TargetFile, "");
            return Task.CompletedTask;
        }

        public override Task ProcessContainersAsync(IList<ContainerRecord> containers)
        {
            var content = GenerateFile(containers);
            UpdateFile(content);

            return Task.CompletedTask;
        }

        private string GenerateFile(IList<ContainerRecord> containers)
        {
            var dnsmasqFile = new StringBuilder();
            var duplicateDetection = new Dictionary<string, string>();


            var dnsRecordsToCreate = new List<DnsZoneRecord>();

            foreach (var container in containers)
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

                    foreach (var domain in splitDomains)
                    {
                        if (duplicateDetection.ContainsKey(domain) && !this.AllowDuplicates)
                        {
                            Logger.LogWarning($"{container.ContainerName} is trying to register '{domain}' which is already owned by Host: '{duplicateDetection[domain]}'");
                            continue;
                        }

                        if (domain == container.Hostname)
                        {
                            Logger.LogError($"Invalid CNAME config for container {container.ContainerName}. Domain and hostname are the same: {domain}");
                            continue;
                        }

                        duplicateDetection[domain] = container.ContainerName;

                        dnsRecordsToCreate.Add(new DnsZoneRecord
                        {
                            Comment = $"{description} -- {container.ContainerName}",
                            Domain = domain,
                            Hostname = container.Hostname,
                            RecordType = container.UseAddressRecords ? "A" : "CNAME",
                            RecordValue = container.Hostname,
                        });
                    }
                }
                else
                {
                    Logger.LogDebug($"{container.ContainerName} DNS generation is disabled");
                }
            }

            var domainLengthPadding = (dnsRecordsToCreate.Select(c => c.Domain.Length).Max()) + 1;
            var recordTypePadding = (dnsRecordsToCreate.Select(c => c.RecordType.Length).Max()) + 1;
            var recordValuePadding = (dnsRecordsToCreate.Select(c => c.RecordValue.Length).Max()) + 1;

            var groupedRecords = dnsRecordsToCreate.GroupBy(c => c.Hostname);
            foreach (var recordsPerHost in groupedRecords.OrderBy(c => c.Key))
            {
                dnsmasqFile.AppendLine($"; =====  {recordsPerHost.Key.ToUpperInvariant()}  =====");

                foreach (var container in recordsPerHost.OrderBy(c => c.Domain))
                {
                    var domain = $"{container.Domain}.".PadRight(domainLengthPadding);
                    var recordValue = container.Hostname.PadRight(recordValuePadding);
                    var recordType = container.RecordType.PadRight(recordTypePadding);
                    dnsmasqFile.AppendLine($"{domain} IN {recordType} {recordValue} ; {container.Comment}");
                }

                dnsmasqFile.AppendLine("");
                dnsmasqFile.AppendLine("");
            }

            Logger.LogDebug("Processing Containers Done");
            return dnsmasqFile.ToString();
        }

        private void UpdateFile(string content)
        {
            var previousFile = System.IO.File.ReadAllText(this.TargetFile);

            if (content != previousFile)
            {
                Logger.LogWarning("DNS Zone File Changed");
                previousFile = content;
                System.IO.File.WriteAllText(this.TargetFile, content);
            }
            else
            {
                Logger.LogDebug("No change to DNS Zone File");
            }
        }

        public override string GetPluginName()
        {
            return PluginName;
        }

        private record DnsZoneRecord
        {
            public DnsZoneRecord()
            {
                Domain = "";
                Hostname = "";
                Comment = "";
                RecordType = "";
                RecordValue = "";
            }

            public string Domain { get; init; }
            public string Hostname { get; init; }
            public string Comment { get; init; }
            public string RecordType { get; init; }
            public string RecordValue { get; init; }
        }
    }
}