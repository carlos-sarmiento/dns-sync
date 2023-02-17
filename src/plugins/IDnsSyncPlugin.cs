using System.Collections.Generic;
using System.Threading.Tasks;

namespace dns_sync
{

    public interface IDnsSyncPlugin
    {
        internal Task ProcessContainers(DnsSyncConfig config, IList<ContainerRecord> containers);
    }

}