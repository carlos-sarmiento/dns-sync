using System;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace dns_sync
{
    public static class DnsSyncLogger
    {
        private static LogEventLevel defaultLogLevel = LogEventLevel.Debug;
        private static ILogger defaultLogger;

        private static OpenObserveConfig? openObserveSinkConfig;

        static DnsSyncLogger()
        {
            defaultLogger = GetLogger<Program>();
        }

        public static void SetDefaultLogLevel(LogEventLevel level)
        {
            defaultLogLevel = level;
            defaultLogger = GetLogger<Program>(level);
        }

        public static void SetOpenObserveSinkConfig(OpenObserveConfig? config)
        {
            openObserveSinkConfig = config;
        }

        public static ILogger GetLogger<T>(LogEventLevel? level = null, bool writeToConsole = true)
        {
            string outputTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss}][{Level:u}][{SourceContext:l}] {Message:lj}{NewLine}{Exception}";

            var logger = new LoggerConfiguration()
                .MinimumLevel.Is(level ?? defaultLogLevel)
                .Enrich.WithProperty("SourceContext", typeof(T).FullName)
                .Enrich.FromLogContext();

            if (writeToConsole)
            {
                logger = logger.WriteTo.Console(
                  theme: AnsiConsoleTheme.Code,
                  outputTemplate: outputTemplate,
                  restrictedToMinimumLevel: LogEventLevel.Information
              );
            }

            if (openObserveSinkConfig != null)
            {
                logger =
                logger
                    .Enrich.WithProperty("instance_host", openObserveSinkConfig.InstanceHost)
                    .WriteTo.OpenObserve(
                        url: openObserveSinkConfig.Url,
                        organization: openObserveSinkConfig.Organization,
                        streamName: openObserveSinkConfig.Stream,
                        login: openObserveSinkConfig.Username,
                        key: openObserveSinkConfig.Password
                    );
            }

            return logger.CreateLogger();
        }

        public static void Critical(string message, Exception? exception = null, params object?[] args)
        {
            defaultLogger.Fatal(exception, message, args);
        }

        public static void Debug(string message, Exception? exception = null, params object?[] args)
        {
            defaultLogger.Debug(exception, message, args);
        }

        public static void Error(string message, Exception? exception = null, params object?[] args)
        {
            defaultLogger.Error(exception, message, args);
        }

        public static void Information(string message, Exception? exception = null, params object?[] args)
        {
            defaultLogger.Information(exception, message, args);
        }

        public static void Trace(string message, Exception? exception = null, params object?[] args)
        {
            defaultLogger.Verbose(exception, message, args);
        }

        public static void Warning(string message, Exception? exception = null, params object?[] args)
        {
            defaultLogger.Warning(exception, message, args);
        }
    }
}



