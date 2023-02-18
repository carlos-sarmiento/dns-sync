using System;
using Microsoft.Extensions.Logging;

namespace dns_sync
{
    public static class DnsSyncLogger
    {

        private static LogLevel defaultLogLevel = LogLevel.Debug;
        private static ILogger defaultLogger;

        static DnsSyncLogger()
        {
            defaultLogger = GetLogger<Program>();
        }

        private static ILoggerFactory GetLoggerFactoryForLevel(LogLevel level)
        {
            return LoggerFactory.Create(builder =>
                                           {
                                               builder
                                                   .SetMinimumLevel(level)
                                                   .AddSimpleConsole(options =>
                                                   {
                                                       options.IncludeScopes = false;
                                                       options.SingleLine = true;
                                                       options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                                                   });
                                           });
        }

        public static void SetDefaultLogLevel(LogLevel level)
        {
            defaultLogLevel = level;
            defaultLogger = GetLogger<Program>(level);
        }

        public static ILogger GetLogger<T>(LogLevel? level = null)
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



