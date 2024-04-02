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
            Server = new DnsServer();
        }

        private DnsClient? UpstreamClient { get; set; }

        private bool ForwardUnmatched { get; set; }

        private DnsServer Server { get; set; }

        private ConcurrentDictionary<DomainName, DnsRecord> Records = new();

        private Dictionary<string, string> DomainRewritingRules = new();

        public static string PluginName => "dnsserver";

        private bool LogQueries { get; set; }

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
                throw new Exception($"Invalid Port provided for DNS-Server Plugin: {rawConfig["port"]}");
            }

            var isValidUpstreamPortNumber = int.TryParse(rawConfig.GetValueOrDefault("upstream_server_port", "53").ToString(), out var upstreamPortNumber);
            if (!isValidUpstreamPortNumber || upstreamPortNumber <= 0 || upstreamPortNumber > 65535)
            {
                throw new Exception($"Invalid Upstream Port provided for DNS-Server Plugin: {rawConfig["upstream_server_port"]}");
            }

            var rawUpstreamServerIp = rawConfig.GetValueOrDefault("upstream_server_ip") as string;
            var isValidUpstreamServer = IPAddress.TryParse(rawUpstreamServerIp, out var upstreamServer);
            if (rawUpstreamServerIp != null && (!isValidUpstreamServer || upstreamServer == null))
            {
                throw new Exception($"Invalid Upstream Server provided for DNS-Server Plugin: {rawConfig["upstream_server_ip"]}");
            }
            else if (upstreamServer != null)
            {
                Logger.LogInformation($"Setting Upstream DNS Server for DNS-Server Plugin: '{rawUpstreamServerIp}:{upstreamPortNumber}'");

                UpstreamClient = new DnsClient(
                                           new[] { upstreamServer },
                                           new IClientTransport[] { new UdpClientTransport(upstreamPortNumber), new TcpClientTransport(upstreamPortNumber), },
                                           false,
                                           5000
                                       );
            }
            else
            {
                UpstreamClient = null;
            }

            var isValidForward = bool.TryParse(rawConfig.GetValueOrDefault("forward_unmatched", "true").ToString(), out var forwardUnmatched);
            if (!isValidForward)
            {
                throw new Exception($"Invalid Value for Forward Unmatched Requests provided for DNS-Server Plugin: {rawConfig["forward_unmatched"]}");
            }
            else
            {
                ForwardUnmatched = forwardUnmatched;
            }

            var isValidLogQueries = bool.TryParse(rawConfig.GetValueOrDefault("log_queries", "false").ToString(), out var logQueries);
            if (!isValidLogQueries)
            {
                throw new Exception($"Invalid Value for Log Queries Unmatched Requests provided for DNS-Server Plugin: {rawConfig["log_queries"]}");
            }
            else
            {
                LogQueries = logQueries;
            }
            var rewrittenDomains = rawConfig.GetValueOrDefault("rewritten_domains", new Dictionary<object, object>()) as Dictionary<object, object>;
            if (rewrittenDomains == null || rewrittenDomains.Any(kvp => kvp.Value is not string || kvp.Key is not string))
            {
                throw new Exception($"Invalid Value for Rewritten Domains provided for DNS-Server Plugin. It must be a map of original domain to rewritten domain");
            }
            else
            {
                DomainRewritingRules = rewrittenDomains.ToDictionary(kvp => kvp.Key.ToString() ?? "", kvp => kvp.Value.ToString() ?? "");
            }

            Server = new DnsServer(new UdpServerTransport(new IPEndPoint(IPAddress.Any, portNumber), 5000),
                new TcpServerTransport(new IPEndPoint(IPAddress.Any, portNumber), 5000, 120000));
            Server.QueryReceived += OnQueryReceived;
            Logger.LogInformation($"Starting DNS Server on Port: '{portNumber}'");

            Server.Start();

            return Task.CompletedTask;
        }

        private void LogQueryDebug(string message)
        {
            if (LogQueries)
            {
                Logger.LogDebug(message);
            }
        }

        private void LogQueryInformation(string message)
        {
            if (LogQueries)
            {
                Logger.LogInformation(message);
            }
        }

        private async Task OnQueryReceived(object sender, QueryReceivedEventArgs request)
        {


            if (request.Query is not DnsMessage query)
            {
                return;
            }

            var response = query.CreateResponseInstance();
            request.Response = response;

            response.IsRecursionAllowed = true;

            if (query.Questions.Count != 1)
            {
                LogQueryDebug($"Received Query with more than one question");

                response.ReturnCode = ReturnCode.ServerFailure;
                return;
            }

            response.ReturnCode = ReturnCode.NoError;

            var question = query.Questions[0];

            LogQueryInformation($"Received Query for RecordType {Enum.GetName(typeof(RecordType), question.RecordType)} on {question.Name}");

            if (question.RecordType == RecordType.Aaaa)
            {
                response.ReturnCode = ReturnCode.NoError;
                return;
            }

            if (question.RecordType != RecordType.A && question.RecordType != RecordType.CName)
            {
                await ForwardTransparentQuery(query.TransactionID, question, response);
                return;
            }

            if (Records.TryGetValue(question.Name, out DnsRecord? value))
            {
                LogQueryInformation($"Replying on Query with Response {value.Response} from server: {value.Container}");

                switch (value.RecordType)
                {
                    case RecordType.CName:
                        await ResolveCname(value.Domain, value.Response, response);
                        break;
                    case RecordType.A:
                        response.AnswerRecords.Add(new ARecord(value.Domain, 60, IPAddress.Parse(value.Response)));
                        break;
                    default:
                        throw new Exception("Unhandled Record Type");
                }

                return;
            }

            if (ForwardUnmatched)
            {
                await ForwardTransparentQuery(query.TransactionID, question, response);
                return;
            }

            LogQueryInformation($"No record found for '{question.Name}'");
            response.ReturnCode = ReturnCode.NxDomain;
        }

        private async Task ForwardRewrittenQuery(DnsQuestion originalQuestion, DnsQuestion rewrittenQuestion, DnsMessage response)
        {
            if (!ForwardUnmatched || UpstreamClient == null)
            {
                response.ReturnCode = ReturnCode.ServerFailure;
                return;
            }

            LogQueryInformation($"Forwarding Query for RecordType {Enum.GetName(typeof(RecordType), rewrittenQuestion.RecordType)} on {rewrittenQuestion.Name}");

            var answer = await UpstreamClient.ResolveAsync(rewrittenQuestion.Name, rewrittenQuestion.RecordType, rewrittenQuestion.RecordClass);

            if (answer != null)
            {
                response.AnswerRecords.Add(new CNameRecord(originalQuestion.Name, 0, rewrittenQuestion.Name));

                foreach (DnsRecordBase record in answer.AnswerRecords)
                {
                    response.AnswerRecords.Add(record);
                }
                // foreach (DnsRecordBase record in answer.AdditionalRecords)
                // {
                //     response.AdditionalRecords.Add(record);
                // }

                response.ReturnCode = answer.ReturnCode;
            }
            else
            {
                response.ReturnCode = ReturnCode.NxDomain;
            }
        }


        private async Task ForwardTransparentQuery(ushort id, DnsQuestion question, DnsMessage response)
        {
            if (!ForwardUnmatched || UpstreamClient == null)
            {
                response.ReturnCode = ReturnCode.ServerFailure;
                return;
            }

            LogQueryInformation($"Forwarding Query {id} for RecordType {Enum.GetName(typeof(RecordType), question.RecordType)} on {question.Name}");

            var answer = await UpstreamClient.ResolveAsync(question.Name, question.RecordType, question.RecordClass);

            if (answer != null && answer.AnswerRecords.Count > 0)
            {
                LogQueryInformation($"Received answer for Query {id}");

                foreach (DnsRecordBase record in answer.AnswerRecords)
                {
                    LogQueryInformation($"Query {id}: {record}");
                    response.AnswerRecords.Add(record);
                }


                response.ReturnCode = answer.ReturnCode;
            }
            else
            {
                LogQueryInformation($"Received NO answer for Query {id}");

                var domainToRewrite = DomainRewritingRules.Keys.FirstOrDefault(x => question.Name.IsEqualOrSubDomainOf(DomainName.Parse(x)));
                if (domainToRewrite != null)
                {
                    var targetDomain = DomainRewritingRules[domainToRewrite];
                    var rewrittenQuestion = new DnsQuestion(
                        DomainName.Parse(question.Name.ToString().Replace(domainToRewrite, targetDomain)),
                        question.RecordType,
                        question.RecordClass
                    );

                    await ForwardRewrittenQuery(question, rewrittenQuestion, response);
                    return;
                }

                response.ReturnCode = ReturnCode.NxDomain;
            }
        }

        private async Task ResolveCname(DomainName original, string targetDomain, DnsMessage response)
        {
            var target = DomainName.Parse(targetDomain);
            response.AnswerRecords.Add(new CNameRecord(original, 0, target));

            if (UpstreamClient == null)
            {
                return;
            }
            response.ReturnCode = ReturnCode.NoError;

            var answer = await UpstreamClient.ResolveAsync(target, RecordType.A);

            if (answer != null)
            {
                foreach (DnsRecordBase record in answer.AnswerRecords)
                {
                    response.AnswerRecords.Add(record);
                }
                // foreach (DnsRecordBase record in answer.AdditionalRecords)
                // {
                //     response.AdditionalRecords.Add(record);
                // }
            }
            else
            {
                response.ReturnCode = ReturnCode.ServerFailure;
            }
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