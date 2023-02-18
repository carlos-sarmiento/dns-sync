using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace dns_sync.plugins
{

    public abstract class DnsSyncPluginBase : IDnsSyncPlugin
    {
        private static ILogger defaultLogger = DnsSyncLogger.GetLogger<DnsSyncPluginBase>();

        protected ILogger Logger { get; private set; }

        protected DnsSyncPluginBase()
        {
            Logger = defaultLogger;
        }

        protected Task ConfigureBaseAsync<T>(Dictionary<string, object> rawConfig)
        {
            LogLevel? logLevel = null;
            if (Enum.TryParse<LogLevel>(rawConfig.GetValueOrDefault("log_level") as string, true, out var parsedLogLevel))
            {
                logLevel = parsedLogLevel;
            }
            Logger = DnsSyncLogger.GetLogger<T>(logLevel);

            if (logLevel != null)
            {
                Logger.LogWarning($"Log Level for plugin `{GetPluginName()}` set to: {logLevel}");
            }

            return Task.CompletedTask;
        }

        public abstract string GetPluginName();
        public abstract Task ProcessContainersAsync(IList<ContainerRecord> containers);
        public abstract Task ConfigureAsync(Dictionary<string, object> rawConfig);
    }
}