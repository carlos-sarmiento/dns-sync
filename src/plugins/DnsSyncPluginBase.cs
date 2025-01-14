using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;

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
            LogEventLevel? logLevel = null;
            if (Enum.TryParse<LogEventLevel>(rawConfig.GetValueOrDefault("log_level") as string, true, out var parsedLogLevel))
            {
                logLevel = parsedLogLevel;
            }
            Logger = DnsSyncLogger.GetLogger<T>(logLevel);

            if (logLevel != null)
            {
                Logger.Warning($"Log Level for plugin `{GetPluginName()}` set to: {logLevel}");
            }

            return Task.CompletedTask;
        }

        public abstract string GetPluginName();
        public abstract Task ProcessContainersAsync(IList<ContainerRecord> containers);
        public abstract Task ConfigureAsync(Dictionary<string, object> rawConfig);
    }
}