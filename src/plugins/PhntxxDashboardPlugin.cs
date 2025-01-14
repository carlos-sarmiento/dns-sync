using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace dns_sync.plugins
{

    public class PhntxxDashboardPlugin : DnsSyncPluginBase
    {
        public static string PluginName => "phntxx_dashboard";

        public override string GetPluginName()
        {
            return PluginName;
        }

        public PhntxxDashboardPlugin()
        {
            TargetFile = "";
        }

        private string TargetFile { get; set; }

        public override Task ConfigureAsync(Dictionary<string, object> rawConfig)
        {
            this.ConfigureBaseAsync<PhntxxDashboardPlugin>(rawConfig);

            var targetFile = rawConfig.GetValueOrDefault("target_file") as string;
            if (string.IsNullOrWhiteSpace(targetFile))
            {
                throw new Exception($"Invalid target_file provided for phntxx_dashboard plugin: {rawConfig["target_file"].ToString()}");
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

            var dnsmasqContent = GenerateDashboardFile(containers);
            UpdateDashboardAppFile(dnsmasqContent);

            return Task.CompletedTask;
        }

        private string GenerateDashboardFile(IList<ContainerRecord> recordsToCreate)
        {
            var categories = new Dictionary<string, List<object>>();
            foreach (var container in recordsToCreate)
            {
                if (!container.IsActiveForDnsSync || !container.GetLabelAsBool("show_in_dashboard", defaultValue: true))
                {
                    continue;
                }

                var category = container.GetLabel(new[] { "phntxx_dashboard.category", "category" }) ?? container.Hostname;
                var displayUrl = container.GetLabel(new[] { "phntxx_dashboard.display_url", "display_url", "dns.domains", "domains" }) ?? "";
                var url = container.GetLabel(new[] { "phntxx_dashboard.url", "url", "dns.domains", "domains" }) ?? "";
                var icon = container.GetLabel(new[] { "phntxx_dashboard.icon", "icon" }) ?? "tv";
                var description = container.GetLabel("description") ?? displayUrl;

                if (string.IsNullOrWhiteSpace(url))
                {
                    Logger.Error($"Container: {container.ContainerName} on {container.Hostname} has no valid url value.");
                    continue;
                }

                if (url.Contains(','))
                {
                    url = url.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
                }

                if (!categories.ContainsKey(category))
                {
                    categories[category] = new List<object>();
                }

                categories[category].Add(new
                {
                    name = description,
                    displayURL = displayUrl,
                    url = $"https://{url}",
                    icon = icon,
                    newTab = true,
                });
            }

            var categoryList = categories.Select(val => new
            {
                name = val.Key,
                items = val.Value
            }).ToList();

            return JsonSerializer.Serialize(new
            {
                categories = categoryList
            },
            new JsonSerializerOptions { WriteIndented = true });
        }

        private bool UpdateDashboardAppFile(string newContent)
        {
            var targetFile = this.TargetFile;

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
                Logger.Information("PhntxxDashboard File Updated");
                previousFile = newContent;

                System.IO.File.WriteAllText(targetFile, newContent);
                return true;
            }
            else
            {
                Logger.Debug("No change to PhntxxDashboard File");
                return false;
            }
        }
    }
}