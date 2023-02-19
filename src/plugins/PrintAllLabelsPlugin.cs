using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace dns_sync.plugins
{

    public class PrintAllLabelsPlugin : DnsSyncPluginBase
    {
        public static string PluginName => "print_all_labels";

        public override string GetPluginName()
        {
            return PluginName;
        }

        private bool RunOnce { get; set; }

        private bool HasRun { get; set; }

        public override Task ConfigureAsync(Dictionary<string, object> rawConfig)
        {
            this.ConfigureBaseAsync<PrintAllLabelsPlugin>(rawConfig);

            var runOnce = rawConfig.GetValueOrDefault("run_once") as string;
            RunOnce = bool.TryParse(runOnce, out var runOnceParsed) ? runOnceParsed : true;

            return Task.CompletedTask;
        }

        public override Task ProcessContainersAsync(IList<ContainerRecord> containers)
        {
            if (RunOnce && HasRun)
            {
                return Task.CompletedTask;
            }

            Dictionary<string, int> labels = new Dictionary<string, int>();
            foreach (var container in containers)
            {
                foreach (var label in container.Labels.Keys)
                {
                    if (!labels.ContainsKey(label))
                    {
                        labels[label] = 0;
                    }

                    labels[label]++;
                }
            }

            var str = new StringBuilder();
            foreach (var label in labels.OrderByDescending(c => c.Value))
            {
                str.AppendLine($"{label.Key}: {label.Value}");
            }

            Logger.LogInformation("All Labels:");
            Console.WriteLine(str.ToString());

            HasRun = true;
            return Task.CompletedTask;
        }
    }
}