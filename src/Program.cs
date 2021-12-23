using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Docker.DotNet.X509;
using Microsoft.Extensions.Logging;

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

        static async Task Exec(string[] args)
        {
            DnsSyncLogger.LogCritical("Starting Up");
            var config = DnsSyncConfig.LoadAndValidate("../config.yml");

            DnsSyncLogger.Initialize(config.LogLevel ?? LogLevel.Debug);

            CertificateCredentials? credentials = null;
            if (config.Auth?.MutualTls != null)
            {
                DnsSyncLogger.LogInformation("Creating TLS Client Auth Credentials");
                var cert = new X509Certificate2(
                             config.Auth.MutualTls.PfxFile,
                             config.Auth.MutualTls.Password);
                credentials = new CertificateCredentials(cert);
            }

            List<DockerHost> hostsToMonitor = config.Hosts?.Select(hostConfig =>
             {
                 var hostUri = new Uri(hostConfig.Uri);
                 var isMTLS = hostUri.Scheme == "https" || hostUri.Scheme == "unix";

                 return hostConfig.IpAddress == null
                        ? new DockerHost(hostUri, hostConfig.Hostname, false, isMTLS ? credentials : null)
                        : new DockerHost(hostUri, hostConfig.IpAddress, true, isMTLS ? credentials : null);

             }).ToList() ?? new List<DockerHost>();


            var dnsmasqUri = config.Dnsmasq?.HostUri;
            var dnsmasqContainerName = config.Dnsmasq?.ContainerName;
            DockerHost? dnsmasqDockerHost = null;

            if (dnsmasqUri != null && dnsmasqContainerName != null)
            {
                var hostUri = new Uri(dnsmasqUri);
                var isMTLS = hostUri.Scheme == "https" || hostUri.Scheme == "unix";

                dnsmasqDockerHost = new DockerHost(hostUri, "", false, isMTLS ? credentials : null);
            }

            while (!SigtermCalled)
            {

                var dnsmasqContent = await GenerateDnsMasqFile(hostsToMonitor);
                var targetFile = config.Dnsmasq?.TargetFile ?? "";

                var previousFile = System.IO.File.ReadAllText(targetFile);

                if (dnsmasqContent != previousFile)
                {
                    DnsSyncLogger.LogWarning("DNSMasq File Changed");
                    previousFile = dnsmasqContent;

                    System.IO.File.WriteAllText(targetFile, dnsmasqContent);

                    if (dnsmasqDockerHost != null && dnsmasqContainerName != null)
                    {
                        DnsSyncLogger.LogWarning("Restarting DNSMasq Container");
                        await dnsmasqDockerHost.RestartContainer(dnsmasqContainerName);
                    }
                }
                else
                {
                    DnsSyncLogger.LogDebug("No change to DNSMasq File");
                }

                DnsSyncLogger.LogDebug($"Waiting for {config.ScanFrequency} seconds");
                await Task.Delay(TimeSpan.FromSeconds(config.ScanFrequency));
            }
        }

        internal static async Task<string> GenerateDnsMasqFile(List<DockerHost> hostsToMonitor)
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
                        dnsmasqFile.AppendLine($"# {container.ContainerName}");

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
    }
}



