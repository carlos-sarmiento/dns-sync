using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using ARSoft.Tools.Net.Dns;
using System.Net;
using ARSoft.Tools.Net;
using Serilog;
using Serilog.Context;

namespace dns_sync.plugins
{

    public class QueryLogger
    {
        public QueryLogger(bool logQueries, ILogger logger)
        {
            LogQueries = logQueries;
            Logger = logger;
            RequestID = Guid.NewGuid().ToString("N");
            Name = "";
        }

        protected bool LogQueries { get; }
        protected ILogger Logger { get; }
        protected string RequestID { get; }
        public string Name { get; set; }

        public void Debug(string message)
        {
            if (LogQueries)
            {
                using (LogContext.PushProperty("request_id", RequestID))
                using (LogContext.PushProperty("domain", Name))
                {
                    Logger.Debug(message);
                }
            }
        }

        public void Error(string message)
        {
            if (LogQueries)
            {
                using (LogContext.PushProperty("request_id", RequestID))
                using (LogContext.PushProperty("domain", Name))
                {
                    Logger.Error(message);
                }
            }
        }
        public void Warning(string message)
        {
            if (LogQueries)
            {
                using (LogContext.PushProperty("request_id", RequestID))
                using (LogContext.PushProperty("domain", Name))
                {
                    Logger.Warning(message);
                }
            }
        }

        public void Information(string message)
        {
            if (LogQueries)
            {
                using (LogContext.PushProperty("request_id", RequestID))
                using (LogContext.PushProperty("domain", Name))
                {
                    Logger.Information(message);
                }
            }
        }
    }

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

        private ConcurrentDictionary<DomainName, List<DnsRecord>> Records = new();

        private Dictionary<string, string> DomainRewritingRules = [];

        public static string PluginName => "dnsserver";

        private bool LogQueries { get; set; }

        private bool FlattenCnames { get; set; }

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
                Logger.Information($"Setting Upstream DNS Server for DNS-Server Plugin: '{rawUpstreamServerIp}:{upstreamPortNumber}'");

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
                throw new Exception($"Invalid Value for Log Queries provided for DNS-Server Plugin: {rawConfig["log_queries"]}");
            }
            else
            {
                LogQueries = logQueries;
            }

            var isValidFlattenRecords = bool.TryParse(rawConfig.GetValueOrDefault("flatten_cnames", "true").ToString(), out var flattenRecords);
            if (!isValidFlattenRecords)
            {
                throw new Exception($"Invalid Value for Flatten CNames provided for DNS-Server Plugin: {rawConfig["flatten_cnames"]}");
            }
            else
            {
                FlattenCnames = flattenRecords;
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
            Logger.Information($"Starting DNS Server on Port: '{portNumber}'");

            Server.Start();

            return Task.CompletedTask;
        }


        private async Task OnQueryReceived(object sender, QueryReceivedEventArgs request)
        {
            var queryLogger = new QueryLogger(this.LogQueries, Logger);

            if (request.Query is not DnsMessage query)
            {
                return;
            }

            var response = query.CreateResponseInstance();
            request.Response = response;
            response.IsRecursionAllowed = true;

            if (query.Questions.Count != 1)
            {
                queryLogger.Error($"Received Query with more than one question");

                response.ReturnCode = ReturnCode.ServerFailure;
                return;
            }

            response.ReturnCode = ReturnCode.NoError;

            var question = query.Questions[0];
            queryLogger.Name = question.Name.ToString();
            queryLogger.Information($"Received Query for RecordType {Enum.GetName(typeof(RecordType), question.RecordType)} on {question.Name}");

            if (question.RecordType == RecordType.Aaaa)
            {
                response.ReturnCode = ReturnCode.NoError;
                return;
            }

            var answers = new List<DnsRecordBase>();

            if (question.RecordType != RecordType.A && question.RecordType != RecordType.CName)
            {
                queryLogger.Warning($"Record type on Question is neither A nor CNAME: {Enum.GetName(typeof(RecordType), question.RecordType)}");
                answers = await ForwardQuery(query.TransactionID, question, queryLogger);
            }
            else if (Records.TryGetValue(question.Name, out List<DnsRecord>? values))
            {
                foreach (var value in values ?? [])
                {
                    queryLogger.Information($"Replying on Query with Response {value.Response} from server: {value.Container}");

                    switch (value.RecordType)
                    {
                        case RecordType.CName:
                            answers.AddRange(await ResolveCname(query.TransactionID, value.Domain, value.Response, queryLogger));
                            break;
                        case RecordType.A:
                            answers.Add(new ARecord(value.Domain, 60, IPAddress.Parse(value.Response)));
                            break;
                        default:
                            throw new Exception("Unhandled Record Type");
                    }
                }
            }
            else if (ForwardUnmatched)
            {
                queryLogger.Information($"Forwarding an Unmatched query: {question.Name}");
                answers = await ForwardQuery(query.TransactionID, question, queryLogger);
            }

            if (answers.Count > 0)
            {
                queryLogger.Information($"Forwarding an Unmatched query: {question.Name}");
                response.AnswerRecords.AddRange(answers);
            }
            else
            {
                queryLogger.Warning($"No record found for '{question.Name}'");
                response.ReturnCode = ReturnCode.NxDomain;
            }
        }

        private async Task<List<DnsRecordBase>> ForwardRewrittenQuery(ushort id, DnsQuestion originalQuestion, DnsQuestion rewrittenQuestion, QueryLogger queryLogger)
        {
            queryLogger.Debug($"Forwarding Rewritten Query for: {rewrittenQuestion.Name}");
            if (!ForwardUnmatched || UpstreamClient == null)
            {
                return [];
            }

            queryLogger.Information($"Forwarding Query for RecordType {Enum.GetName(typeof(RecordType), rewrittenQuestion.RecordType)} on {rewrittenQuestion.Name}");

            var forwardAnswers = await ForwardQuery(id, rewrittenQuestion, queryLogger);
            var answers = new List<DnsRecordBase>();
            if (forwardAnswers.Count > 0)
            {
                if (!FlattenCnames)
                {
                    answers.Add(new CNameRecord(originalQuestion.Name, 0, rewrittenQuestion.Name));
                }

                foreach (DnsRecordBase record in forwardAnswers)
                {
                    if (FlattenCnames)
                    {
                        if (record is ARecord arecord)
                        {
                            answers.Add(new ARecord(originalQuestion.Name, arecord.TimeToLive, arecord.Address));
                        }
                    }
                    else
                    {
                        answers.Add(record);
                    }
                }

            }
            else
            {
                queryLogger.Warning($"No information found for Rewritten Query for RecordType {Enum.GetName(typeof(RecordType), rewrittenQuestion.RecordType)} on {rewrittenQuestion.Name}");
            }

            return answers;
        }


        private async Task<List<DnsRecordBase>> ForwardQuery(ushort id, DnsQuestion question, QueryLogger queryLogger)
        {
            queryLogger.Debug($"Forwarding Query for: {question.Name}");
            if (!ForwardUnmatched || UpstreamClient == null)
            {
                return [];
            }

            var answers = new List<DnsRecordBase>();


            var domainToRewrite = DomainRewritingRules.Keys.FirstOrDefault(x => question.Name.IsEqualOrSubDomainOf(DomainName.Parse(x)));
            if (domainToRewrite != null)
            {
                queryLogger.Information($"Rewritting Query {id} for RecordType {Enum.GetName(typeof(RecordType), question.RecordType)} on {question.Name}");

                var targetDomain = DomainRewritingRules[domainToRewrite];
                var rewrittenQuestion = new DnsQuestion(
                    DomainName.Parse(question.Name.ToString().Replace(domainToRewrite, targetDomain)),
                    question.RecordType,
                    question.RecordClass
                );

                answers = await ForwardRewrittenQuery(id, question, rewrittenQuestion, queryLogger);
            }

            if (answers.Count == 0)
            {
                queryLogger.Information($"Forwarding Query {id} for RecordType {Enum.GetName(typeof(RecordType), question.RecordType)} on {question.Name}");

                var answer = await UpstreamClient.ResolveAsync(question.Name, question.RecordType, question.RecordClass);

                if (answer != null && answer.AnswerRecords.Count > 0)
                {
                    queryLogger.Information($"Received answer for Query {id}");

                    foreach (DnsRecordBase record in answer.AnswerRecords)
                    {
                        queryLogger.Information($"Query {id}: {record}");
                        answers.Add(record);
                    }
                }
                else
                {
                    queryLogger.Warning($"Received NO answer for Query {id}");
                }
            }

            return answers;
        }

        private async Task<List<DnsRecordBase>> ResolveCname(ushort id, DomainName original, string targetDomain, QueryLogger queryLogger)
        {
            var target = DomainName.Parse(targetDomain);
            var answers = new List<DnsRecordBase>();
            if (!FlattenCnames)
            {
                answers.Add(new CNameRecord(original, 0, target));
            }
            if (UpstreamClient == null)
            {
                return answers;
            }

            var upstreamAnswers = await ForwardQuery(id, new DnsQuestion(DomainName.Parse(targetDomain), RecordType.A, RecordClass.INet), queryLogger);

            queryLogger.Information("Flattening CNames for Query");
            if (upstreamAnswers != null && upstreamAnswers.Count > 0)
            {
                foreach (DnsRecordBase record in upstreamAnswers)
                {
                    if (FlattenCnames)
                    {
                        if (record is ARecord arecord)
                        {
                            answers.Add(new ARecord(original, arecord.TimeToLive, arecord.Address));
                        }
                    }
                    else
                    {
                        answers.Add(record);
                    }
                }
            }
            else
            {
                queryLogger.Warning($"Received NO answer for Query {id}");
            }

            return answers;
        }

        public override Task ProcessContainersAsync(IList<ContainerRecord> containers)
        {
            UpdateRecords(containers);
            return Task.CompletedTask;
        }

        private void UpdateRecords(IList<ContainerRecord> recordsToCreate)
        {
            var tempRecords = new ConcurrentDictionary<DomainName, List<DnsRecord>>();

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
                            Logger.Error($"{container.ContainerName} has no domains to register on DNS.");
                        }


                        var description = container.GetLabel("description") ?? domains;
                        var splitDomains = domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        foreach (var domain in splitDomains)
                        {
                            Logger.Debug($"Processing Domain: '{domain}'");
                            var clasDomain = DomainName.Parse(domain);

                            if (!tempRecords.ContainsKey(clasDomain))
                            {
                                tempRecords[clasDomain] = [];
                            }

                            if (container.UseAddressRecords)
                            {
                                Logger.Debug($"Registering A Record for '{container.ContainerName}' Domain: '{clasDomain.ToString()}' Response: '{container.Hostname}'");
                                tempRecords[clasDomain].Add(new DnsRecord()
                                {
                                    Container = container,
                                    Domain = clasDomain,
                                    RecordType = RecordType.A,
                                    Response = container.Hostname
                                });
                            }
                            else
                            {
                                if (domain != hostname)
                                {
                                    Logger.Debug($"Registering CNAME for '{container.ContainerName}' Domain: '{clasDomain.ToString()}' Response: '{container.Hostname}'");
                                    tempRecords[clasDomain].Add(new DnsRecord()
                                    {
                                        Container = container,
                                        Domain = clasDomain,
                                        RecordType = RecordType.CName,
                                        Response = container.Hostname
                                    });
                                }
                                else
                                {
                                    Logger.Error($"Invalid CNAME config for container {container.ContainerName}. Domain and hostname are the same: {domain}");
                                }
                            }
                        }
                    }
                    else
                    {
                        Logger.Debug($"{container.ContainerName} DNS generation is disabled");
                    }
                }
            }

            Records = tempRecords;

            Logger.Debug("Processing Containers Done");
        }
    }
}