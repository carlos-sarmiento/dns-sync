using System.Collections.Generic;
using System.Threading.Tasks;

namespace dns_sync.plugins
{

    public interface IDnsSyncPlugin
    {
        public static string PluginName => "";

        public string GetPluginName();

        public Task ConfigureAsync(Dictionary<string, object> rawConfig);

        public Task ProcessContainersAsync(IList<ContainerRecord> containers);
    }

}