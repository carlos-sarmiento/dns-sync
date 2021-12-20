using System;
using Microsoft.Extensions.Logging;

namespace dns_sync
{
    public static class DnsSyncLogger
    {
        private static ILogger logger;

        static DnsSyncLogger()
        {
            logger = InitializeImpl(LogLevel.Trace);
        }

        private static ILogger InitializeImpl(LogLevel level)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
                                    {
                                        builder
                                            .AddFilter("Microsoft", LogLevel.Warning)
                                            .AddFilter("System", LogLevel.Warning)
                                            .AddFilter("dns_sync.Program", level)
                                            .AddConsole();
                                    });
            return loggerFactory.CreateLogger<Program>();
        }

        public static void Initialize(LogLevel level)
        {
            logger = InitializeImpl(level);
        }

        static ILogger GetLogger()
        {
            if (logger == null)
            {
                Initialize(LogLevel.Debug);
                throw new Exception("Logger has not been initialized");
            }

            return logger;
        }

        public static void LogCritical(string message, Exception? exception = null, params object?[] args)
        {
            logger.LogCritical(exception, message, args);
        }

        public static void LogDebug(string message, Exception? exception = null, params object?[] args)
        {
            logger.LogDebug(exception, message, args);
        }

        public static void LogError(string message, Exception? exception = null, params object?[] args)
        {
            logger.LogError(exception, message, args);
        }

        public static void LogInformation(string message, Exception? exception = null, params object?[] args)
        {
            logger.LogInformation(exception, message, args);
        }

        public static void LogTrace(string message, Exception? exception = null, params object?[] args)
        {
            logger.LogTrace(exception, message, args);
        }

        public static void LogWarning(string message, Exception? exception = null, params object?[] args)
        {
            logger.LogWarning(exception, message, args);
        }
    }
}



