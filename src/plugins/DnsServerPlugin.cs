using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using ARSoft.Tools.Net.Dns;
using System.Net;
using ARSoft.Tools.Net;

namespace dns_sync.plugins
{

    public class DnsServerPlugin : DnsSyncPluginBase
    {
        protected record DnsRecord
        {
            public DnsRecord()
            {
                Domain = DomainName.Root;
                RecordType = RecordType.A;
                Response = "";
                Container = ContainerRecord.Empty();
            }
            public DomainName Domain { get; set; }

            public ContainerRecord Container { get; set; }

            public RecordType RecordType { get; set; }
            public string Response { get; set; }

        }

        public DnsServerPlugin()
        {
            Port = 53;
        }

        private int Port { get; set; }

        private ConcurrentDictionary<DomainName, DnsRecord> Records = new ConcurrentDictionary<DomainName, DnsRecord>();

        private DnsServer server = new DnsServer();

        public static string PluginName => "dnsserver";

        public override string GetPluginName()
        {
            return PluginName;
        }

        public override Task ConfigureAsync(Dictionary<string, object> rawConfig)
        {
            this.ConfigureBaseAsync<DnsServerPlugin>(rawConfig);
            var isValid = int.TryParse(rawConfig.GetValueOrDefault("port", "53").ToString(), out var portNumber);
            if (!isValid || portNumber <= 0 || portNumber > 65535)
            {
                throw new Exception($"Invalid Port provided for DNS-Server Plugin: {rawConfig["port"].ToString()}");
            }
            else
            {
                this.Port = portNumber;
            }

            // Start the Server on a separate Thread. Make sure we can kill it if required.


            server = new DnsServer(new UdpServerTransport(new IPEndPoint(IPAddress.Any, Port), 5000),
                new TcpServerTransport(new IPEndPoint(IPAddress.Any, Port), 5000, 120000));
            server.QueryReceived += OnQueryReceived;
            Logger.LogInformation($"Starting DNS Server on Port {this.Port}");

            server.Start();

            return Task.CompletedTask;
        }

        private Task OnQueryReceived(object sender, QueryReceivedEventArgs e)
        {
            Logger.LogDebug($"Received Query");

            var query = e.Query as DnsMessage;

            if (query == null)
                return Task.CompletedTask;

            var response = query.CreateResponseInstance();

            var returnCode = ReturnCode.NoError;

            foreach (var q in query.Questions.Take(1))
            {
                Logger.LogDebug($"Received Query for RecordType {Enum.GetName(typeof(RecordType), q.RecordType)} on {q.Name.ToString()}.");

                if (Records.ContainsKey(q.Name))
                {
                    var r = Records[q.Name];

                    if (q.RecordType != RecordType.A && q.RecordType != RecordType.CName)
                    {
                        Logger.LogDebug($"Can't respond to query. RecordTypes do not Match");
                        return Task.CompletedTask;
                    }

                    Logger.LogInformation($"Replying on Query with Response {r.Response} from server: {r.Container.ToString()}");

                    switch (r.RecordType)
                    {
                        case RecordType.CName:
                            response.AnswerRecords.Add(new CNameRecord(r.Domain, 60, DomainName.Parse(r.Response)));
                            break;
                        case RecordType.A:
                            response.AnswerRecords.Add(new ARecord(r.Domain, 60, IPAddress.Parse(r.Response)));
                            break;
                        default:
                            throw new Exception("Unhandled Record Type");
                    }
                }
                else
                {
                    Logger.LogDebug($"No record found for {q.Name.ToString()}");
                    returnCode = ReturnCode.NoError;
                }
            }

            response.ReturnCode = returnCode;

            // set the response
            e.Response = response;

            return Task.CompletedTask;
        }

        public override Task ProcessContainersAsync(IList<ContainerRecord> containers)
        {
            UpdateRecords(containers);
            return Task.CompletedTask;
        }

        private void UpdateRecords(IList<ContainerRecord> recordsToCreate)
        {
            var tempRecords = new ConcurrentDictionary<DomainName, DnsRecord>();

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
                    if (container.IsActiveForDnsSync && container.GetLabelAsBool(new[] { "dnsserver.register", "dns.register", "register_on_dns" }, defaultValue: true))
                    {

                        var domains = container.GetLabel(new[] { "dnsserver.domains", "dns.domains", "domains" }) ?? "";

                        if (string.IsNullOrWhiteSpace(domains))
                        {
                            Logger.LogError($"{container.ContainerName} has no domains to register on DNS.");
                        }


                        var description = container.GetLabel("description") ?? domains;
                        var splitDomains = domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        foreach (var domain in splitDomains)
                        {
                            if (duplicateDetection.ContainsKey(domain))
                            {
                                Logger.LogWarning($"{container.ContainerName} is trying to register '{domain}' which is already owned by Host: '{duplicateDetection[domain]}'");
                                continue;
                            }

                            duplicateDetection[domain] = container.ContainerName;

                            var clasDomain = DomainName.Parse(domain);

                            if (container.UseAddressRecords)
                            {
                                tempRecords[clasDomain] = new DnsRecord()
                                {
                                    Container = container,
                                    Domain = clasDomain,
                                    RecordType = RecordType.A,
                                    Response = container.Hostname
                                };
                            }
                            else
                            {
                                if (domain != hostname)
                                {
                                    tempRecords[clasDomain] = new DnsRecord()
                                    {
                                        Container = container,
                                        Domain = clasDomain,
                                        RecordType = RecordType.CName,
                                        Response = container.Hostname
                                    };
                                }
                                else
                                {
                                    Logger.LogError($"Invalid CNAME config for container {container.ContainerName}. Domain and hostname are the same: {domain}");
                                }
                            }
                        }
                    }
                    else
                    {
                        Logger.LogDebug($"{container.ContainerName} DNS generation is disabled");
                    }
                }
            }

            Records = tempRecords;

            Logger.LogDebug("Processing Containers Done");
        }
    }
}