using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Docker.DotNet.X509;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace dns_sync
{
    class Program
    {
        private static bool SigtermCalled { get; set; }

        static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit += (object? sender, EventArgs e) =>
                  {
                      DnsSyncLogger.LogCritical("Shutting Down");
                      SigtermCalled = true;
                  };

            try
            {
                await Exec(args);
            }
            catch (Exception e)
            {
                DnsSyncLogger.LogError("Unhandled Exception", e);
            }
        }

        static CertificateCredentials? BuildAuthCredentials(DnsSyncConfig config)
        {
            CertificateCredentials? credentials = null;
            if (config.Auth?.MutualTls != null)
            {
                DnsSyncLogger.LogInformation("Creating TLS Client Auth Credentials");
                var cert = new X509Certificate2(
                             config.Auth.MutualTls.PfxFile,
                             config.Auth.MutualTls.Password);
                credentials = new CertificateCredentials(cert);

                if (config.Auth?.DoNotValidateServerCertificate == true)
                {
                    // Disable Certificate Validation for Server Endpoint
                    credentials.ServerCertificateValidationCallback += (o, c, ch, er) => true;
                }
            }

            return credentials;
        }

        static async Task Exec(string[] args)
        {
            DnsSyncLogger.LogCritical("Starting Up");

            var configFileLocation = args.Any() ? args[0] : "/config/config.yml";

            var config = DnsSyncConfig.LoadAndValidate(configFileLocation);

            DnsSyncLogger.Initialize(config.LogLevel ?? LogLevel.Debug);

            var credentials = BuildAuthCredentials(config);

            List<DockerHost> hostsToMonitor = config.Hosts?.Select(hostConfig =>
             {
                 var hostUri = new Uri(hostConfig.Uri);
                 var isMTLS = hostUri.Scheme == "https" || hostUri.Scheme == "unix";

                 return hostConfig.IpAddress == null
                        ? new DockerHost(hostUri, hostConfig.Hostname, false, isMTLS ? credentials : null)
                        : new DockerHost(hostUri, hostConfig.IpAddress, true, isMTLS ? credentials : null);

             }).ToList() ?? new List<DockerHost>();

            while (!SigtermCalled)
            {
                var containers = await GenerateContainers(hostsToMonitor);

                var dnsmasqContent = GenerateDnsMasqFile(containers);
                var wasDnsmasqFileUpdated = UpdateDnsMasqFile(config, dnsmasqContent);

                var dashboardContent = GenerateDashboardFile(containers);
                var wasDashboardFileUpdaed = UpdateDashboardAppFile(config, dashboardContent);

                await RestartDnsmasqInstance(config, wasDnsmasqFileUpdated);

                DnsSyncLogger.LogDebug($"Waiting for {config.ScanFrequency} seconds");
                await Task.Delay(TimeSpan.FromSeconds(config.ScanFrequency));
            }
        }

        static async Task RestartDnsmasqInstance(DnsSyncConfig config, bool wasDnsmasqFileUpdated)
        {
            var dnsmasqUri = config.Dnsmasq?.HostUri;
            var dnsmasqContainerName = config.Dnsmasq?.ContainerName;
            DockerHost? dnsmasqDockerHost = null;

            if (dnsmasqUri != null && dnsmasqContainerName != null)
            {
                var hostUri = new Uri(dnsmasqUri);
                var isMTLS = hostUri.Scheme == "https" || hostUri.Scheme == "unix";

                dnsmasqDockerHost = new DockerHost(hostUri, "", false, isMTLS ? BuildAuthCredentials(config) : null);
            }

            if (wasDnsmasqFileUpdated && dnsmasqDockerHost != null && dnsmasqContainerName != null)
            {
                DnsSyncLogger.LogWarning("Restarting DNSMasq Container");
                await dnsmasqDockerHost.RestartContainer(dnsmasqContainerName);
            }
        }

        internal static bool UpdateDnsMasqFile(DnsSyncConfig config, string newDnsmasqContent)
        {
            var targetFile = config.Dnsmasq?.TargetFile ?? "";

            var previousFile = System.IO.File.ReadAllText(targetFile);

            if (newDnsmasqContent != previousFile)
            {
                DnsSyncLogger.LogWarning("DNSMasq File Changed");
                previousFile = newDnsmasqContent;

                System.IO.File.WriteAllText(targetFile, newDnsmasqContent);
                return true;
            }
            else
            {
                DnsSyncLogger.LogDebug("No change to DNSMasq File");
                return false;
            }
        }

        internal static bool UpdateDashboardAppFile(DnsSyncConfig config, string newContent)
        {
            var targetFile = config.DashboardTargetFile ?? "";

            if (string.IsNullOrWhiteSpace(targetFile))
            {
                return false;
            }

            var previousFile = "";
            try
            {
                previousFile = System.IO.File.ReadAllText(targetFile);
            }
            catch (System.IO.FileNotFoundException)
            {
                previousFile = "";
            }

            if (newContent != previousFile)
            {
                DnsSyncLogger.LogWarning("Dashboard File Changed");
                previousFile = newContent;

                System.IO.File.WriteAllText(targetFile, newContent);
                return true;
            }
            else
            {
                DnsSyncLogger.LogDebug("No change to Dashboard File");
                return false;
            }
        }

        internal static async Task<IList<ContainerDomainRecords>[]> GenerateContainers(List<DockerHost> hostsToMonitor)
        {
            DnsSyncLogger.LogDebug("Fetching Containers");

            IList<ContainerDomainRecords>[] recordsToCreate = (await Task.WhenAll(
                                                                      hostsToMonitor.Select(
                                                                           async host =>
                                                                           {
                                                                               try
                                                                               {
                                                                                   DnsSyncLogger.LogDebug($"Fetching Host: {host.ConnectionUri.ToString()}");
                                                                                   var containersToAlias = await host.GetContainersToAlias();

                                                                                   foreach (var c in containersToAlias)
                                                                                   {
                                                                                       DnsSyncLogger.LogDebug($"Found container: {c.ContainerName} - Enabled: {c.IsMappingEnabled}");
                                                                                   }

                                                                                   return containersToAlias;
                                                                               }
                                                                               catch (Exception e)
                                                                               {
                                                                                   DnsSyncLogger.LogWarning($"Error while fetching containers from {host.ConnectionUri.ToString()}'", e);
                                                                                   return new List<ContainerDomainRecords>();
                                                                               }
                                                                           }
                                                                      ).ToArray()
                                                                  )
                                                              ) ?? new IList<ContainerDomainRecords>[0];


            return recordsToCreate;
        }

        internal static string GenerateDnsMasqFile(IList<ContainerDomainRecords>[] recordsToCreate)
        {
            var dnsmasqFile = new StringBuilder();
            var duplicateDetection = new Dictionary<string, string>();

            DnsSyncLogger.LogDebug("Processing Containers");

            foreach (var recordsPerHost in recordsToCreate)
            {
                if (!recordsPerHost.Any())
                {
                    continue;
                }

                var hostname = recordsPerHost.First().Hostname;


                foreach (var container in recordsPerHost)
                {
                    if (container.IsMappingEnabled)
                    {
                        dnsmasqFile.AppendLine($"# {container.ContainerName} -- {container.Description}");

                        foreach (var domain in container.Domains)
                        {
                            if (duplicateDetection.ContainsKey(domain))
                            {
                                DnsSyncLogger.LogWarning($"{container.ContainerName} is trying to register '{domain}' which is already owned by Host: '{duplicateDetection[domain]}'");
                                continue;
                            }

                            duplicateDetection[domain] = container.ContainerName;
                            if (container.UseAddressRecords)
                            {
                                dnsmasqFile.AppendLine($"address=/{domain}/{hostname}");

                            }
                            else
                            {
                                dnsmasqFile.AppendLine($"cname={domain},{hostname}");
                            }
                        }
                        dnsmasqFile.AppendLine("");
                    }
                    else
                    {
                        DnsSyncLogger.LogDebug($"{container.ContainerName} DNS generation is disabled");
                    }
                }
            }
            DnsSyncLogger.LogDebug("Processing Containers Done");

            return dnsmasqFile.ToString();
        }

        internal static string GenerateDashboardFile(IList<ContainerDomainRecords>[] recordsToCreate)
        {
            var categories = new List<object>();

            foreach (var recordsPerHost in recordsToCreate)
            {
                if (!recordsPerHost.Any())
                {
                    continue;
                }

                var hostname = recordsPerHost.First().Hostname;
                var apps = new List<object>();

                foreach (var container in recordsPerHost)
                {
                    if (container.IsMappingEnabled)
                    {
                        apps.Add(new
                        {
                            name = $"{container.Description}",
                            displayURL = container.Domains.FirstOrDefault() ?? "",
                            url = "https://" + container.Domains.FirstOrDefault() ?? "",
                            icon = "tv",
                            newTab = true,
                        });
                    }
                }

                categories.Add(new
                {
                    name = hostname,
                    items = apps
                });
            }

            return JsonSerializer.Serialize(new
            {
                categories = categories
            },
            new JsonSerializerOptions { WriteIndented = true });
        }
    }
}



