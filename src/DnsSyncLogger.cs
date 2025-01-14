using System;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Sinks.SystemConsole.Themes;
using MSILogger = Microsoft.Extensions.Logging.ILogger;

namespace dns_sync
{

    public static class DnsSyncLogger
    {

        private static LogLevel defaultLogLevel = LogLevel.Debug;
        private static MSILogger defaultLogger;

        private static OpenObserveConfig? openObserveSinkConfig;

        static DnsSyncLogger()
        {
            defaultLogger = GetLogger<Program>();
        }

        private static ILoggerFactory GetLoggerFactoryForLevel(LogLevel level)
        {
            string outputTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss}][{Level:u}][{SourceContext:l}] {Message:lj}{NewLine}{Exception}";

            return LoggerFactory.Create(builder =>
                                           {
                                               var logger = new LoggerConfiguration()
                                                               .MinimumLevel.Is(LevelConvert.ToSerilogLevel(level))
                                                               .WriteTo.Console(
                                                                    theme: AnsiConsoleTheme.Code,
                                                                    outputTemplate: outputTemplate,
                                                                    restrictedToMinimumLevel: LogEventLevel.Information
                                                                );

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

                                               builder.SetMinimumLevel(level)
                                                   .AddSerilog(
                                                           logger.CreateLogger(),
                                                           true
                                                   );
                                           });
        }

        public static void SetDefaultLogLevel(LogLevel level)
        {
            defaultLogLevel = level;
            defaultLogger = GetLogger<Program>(level);
        }

        public static void SetOpenObserveSinkConfig(OpenObserveConfig? config)
        {
            openObserveSinkConfig = config;
        }

        public static MSILogger GetLogger<T>(LogLevel? level = null)
        {
            return GetLoggerFactoryForLevel(level ?? defaultLogLevel).CreateLogger<T>();
        }

        public static void LogCritical(string message, Exception? exception = null, params object?[] args)
        {
            defaultLogger.LogCritical(exception, message, args);
        }

        public static void LogDebug(string message, Exception? exception = null, params object?[] args)
        {
            defaultLogger.LogDebug(exception, message, args);
        }

        public static void LogError(string message, Exception? exception = null, params object?[] args)
        {
            defaultLogger.LogError(exception, message, args);
        }

        public static void LogInformation(string message, Exception? exception = null, params object?[] args)
        {
            defaultLogger.LogInformation(exception, message, args);
        }

        public static void LogTrace(string message, Exception? exception = null, params object?[] args)
        {
            defaultLogger.LogTrace(exception, message, args);
        }

        public static void LogWarning(string message, Exception? exception = null, params object?[] args)
        {
            defaultLogger.LogWarning(exception, message, args);
        }
    }
}



