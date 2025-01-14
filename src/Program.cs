using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Docker.DotNet.X509;
using Microsoft.Extensions.Logging;
using dns_sync.plugins;
using Serilog.Events;

namespace dns_sync
{
    class Program
    {
        private static bool SigtermCalled { get; set; }

        private static List<IDnsSyncPlugin> ActivePlugins => new List<IDnsSyncPlugin>();

        static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit += (object? sender, EventArgs e) =>
                  {
                      DnsSyncLogger.Information("Shutting Down");
                      SigtermCalled = true;
                  };

            try
            {
                await Exec(args);
            }
            catch (Exception e)
            {
                DnsSyncLogger.Critical("Unhandled Exception", e);
            }
        }

        static CertificateCredentials? BuildAuthCredentials(DnsSyncConfig config)
        {
            CertificateCredentials? credentials = null;
            if (config.Auth?.MutualTls != null)
            {
                DnsSyncLogger.Information("Creating TLS Client Auth Credentials");
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
            DnsSyncLogger.Information("Starting Up");

            var configFileLocation = args.Any() ? args[0] : "/config/config.yml";
            var config = DnsSyncConfig.LoadAndValidate(configFileLocation);

            DnsSyncLogger.SetDefaultLogLevel(config.LogLevel ?? LogEventLevel.Debug);
            DnsSyncLogger.SetOpenObserveSinkConfig(config.OpenObserve);

            var credentials = BuildAuthCredentials(config);
            var hostsToMonitor = BuildHostListAsync(config, credentials);

            var plugins = await GetConfiguredPluginsAsync(config);

            while (!SigtermCalled)
            {
                var containers = await GenerateContainers(hostsToMonitor);

                foreach (var plugin in plugins)
                {
                    DnsSyncLogger.Debug($"Running Plugin: {plugin.GetPluginName()}");
                    try
                    {
                        await plugin.ProcessContainersAsync(containers);
                    }
                    catch (Exception e)
                    {
                        DnsSyncLogger.Error($"Error throw when processing plugin: {plugin.GetPluginName()}", e);
                    }
                }

                DnsSyncLogger.Debug($"Waiting for {config.ScanFrequency} seconds");
                await Task.Delay(TimeSpan.FromSeconds(config.ScanFrequency));
            }
        }

        private static IList<DockerHost> BuildHostListAsync(DnsSyncConfig config, CertificateCredentials? credentials)
        {
            return config.Hosts?.Select(hostConfig =>
                 {
                     var hostUri = new Uri(hostConfig.Uri);
                     var isMTLS = hostUri.Scheme == "https" || hostUri.Scheme == "unix";

                     return hostConfig.IpAddress == null
                            ? new DockerHost(hostUri, hostConfig.Hostname, false, isMTLS ? credentials : null)
                            : new DockerHost(hostUri, hostConfig.IpAddress, true, isMTLS ? credentials : null);

                 }).ToList() ?? new List<DockerHost>();
        }

        private static async Task<IList<IDnsSyncPlugin>> GetConfiguredPluginsAsync(DnsSyncConfig config)
        {
            var pluginLibrary = new PluginLibrary()
                .AddPluginsFromAssembly<Program>();

            var pluginsToConfigure = config.Plugins.Keys;

            var configuredPlugins = new List<IDnsSyncPlugin>(config.Plugins.Count);

            foreach (var pluginName in pluginsToConfigure)
            {
                try
                {
                    var plugin = pluginLibrary.GetPlugin(pluginName);
                    await plugin.ConfigureAsync(config.Plugins[pluginName] ?? new Dictionary<string, object>());
                    configuredPlugins.Add(plugin);
                }
                catch (Exception e)
                {
                    DnsSyncLogger.Error($"Error while configuring plugin: {pluginName}: {e.Message}", e);
                }
            }

            return configuredPlugins;
        }

        private static async Task<ContainerRecord[]> GenerateContainers(IList<DockerHost> hostsToMonitor)
        {
            DnsSyncLogger.Information("Updating From All Servers");

            var recordsToCreate = (await Task.WhenAll(
                                        hostsToMonitor.Select(
                                            async host =>
                                            {
                                                try
                                                {
                                                    DnsSyncLogger.Debug($"Fetching Host: {host.ConnectionUri}");
                                                    var containersToAlias = await host.GetContainersToAlias();

                                                    foreach (var c in containersToAlias)
                                                    {
                                                        DnsSyncLogger.Debug($"Found container: {c.ContainerName} - Is Active for DNS-Sync: {c.IsActiveForDnsSync}");
                                                    }

                                                    return containersToAlias;
                                                }
                                                catch (Exception e)
                                                {
                                                    DnsSyncLogger.Error($"Error while fetching containers from {host.ConnectionUri}");
                                                    DnsSyncLogger.Debug(e.ToString());

                                                    return new List<ContainerRecord>();
                                                }
                                            }
                                        ).ToArray()
                                    )
                                );

            DnsSyncLogger.Information("Update Completed");

            return recordsToCreate.SelectMany(t => t).ToArray() ?? new ContainerRecord[0];
        }
    }
}
