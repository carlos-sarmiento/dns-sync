using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace dns_sync.plugins
{

    public class HomerDashboardPlugin : DnsSyncPluginBase
    {
        public static string PluginName => "homer";

        public override string GetPluginName()
        {
            return PluginName;
        }

        public HomerDashboardPlugin()
        {
            TargetFile = "";
            BaseFile = "";
        }

        private string TargetFile { get; set; }
        private string BaseFile { get; set; }

        public override Task ConfigureAsync(Dictionary<string, object> rawConfig)
        {
            this.ConfigureBaseAsync<HomerDashboardPlugin>(rawConfig);

            if (!rawConfig.ContainsKey("base_file"))
            {
                throw new Exception($"base_file key is missing for homer plugin");
            }

            var baseFile = rawConfig.GetValueOrDefault("base_file") as string;

            if (string.IsNullOrWhiteSpace(baseFile))
            {
                throw new Exception($"Invalid base_file provided for homer plugin: {rawConfig["base_file"].ToString()}");
            }
            else
            {
                this.BaseFile = baseFile;
            }


            var targetFile = rawConfig.GetValueOrDefault("target_file") as string;
            if (string.IsNullOrWhiteSpace(targetFile))
            {
                throw new Exception($"Invalid target_file provided for homer plugin: {rawConfig["target_file"].ToString()}");
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

                var category = container.GetLabel(new[] { "homer.category", "category" }) ?? container.Hostname;
                var displayUrl = container.GetLabel(new[] { "homer.display_url", "display_url", "dns.domains", "domains" }) ?? "";
                var url = container.GetLabel(new[] { "homer.url", "url", "dns.domains", "domains" }) ?? "";
                var icon = container.GetLabel(new[] { "homer.icon", "icon" });
                var description = container.GetLabel("description") ?? displayUrl;
                var type = container.GetLabel("homer.type") ?? "Ping";

                if (!string.IsNullOrWhiteSpace(icon) && icon.StartsWith("dai:"))
                {
                    icon = $"https://cdn.jsdelivr.net/gh/walkxcode/dashboard-icons/png/{icon.Substring(4)}.png";
                }

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
                    logo = icon,
                    subtitle = container.ContainerName,
                    type = type,
                    tag = (string?)null,
                    keywords = new string[0],
                    url = $"https://{url}",
                    target = "_blank",
                    method = "GET"
                });
            }

            var categoryList = categories.Select(val => new
            {
                name = val.Key,
                icon = "",
                items = val.Value
            }).ToList();

            var serializer = new SerializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            var baseData = LoadBaseFile();

            List<object> services;
            if (baseData.ContainsKey("services"))
            {
                services = baseData["services"] as List<object> ?? new List<object>();
                services.AddRange(categoryList);
            }
            else
            {
                services = new List<object>();
                services.AddRange(categoryList);
            }

            baseData["services"] = services;

            return serializer.Serialize(baseData);
        }

        private Dictionary<string, object> LoadBaseFile()
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            var ymlText = System.IO.File.ReadAllText(BaseFile);

            return deserializer.Deserialize<Dictionary<string, object>>(ymlText) ?? new Dictionary<string, object>();
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
                Logger.Information("Homer File Updated");
                previousFile = newContent;

                System.IO.File.WriteAllText(targetFile, newContent);
                return true;
            }
            else
            {
                Logger.Debug("No change to Homer File");
                return false;
            }
        }
    }
}