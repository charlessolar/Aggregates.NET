using System;
using NServiceBus.Logging;

namespace Aggregates.Extensions
{
    public static class LoggerExtensions
    {
        public static void WriteFormat(this ILog logger, LogLevel level, string message, params object[] args)
        {
            if (Defaults.MinimumLogging.Value.HasValue)
                level = level < Defaults.MinimumLogging.Value ? Defaults.MinimumLogging.Value.Value : level;

            switch (level)
            {
                case LogLevel.Debug:
                    logger.DebugFormat(message, args);
                    break;
                case LogLevel.Info:
                    logger.InfoFormat(message, args);
                    break;
                case LogLevel.Warn:
                    logger.WarnFormat(message, args);
                    break;
                case LogLevel.Error:
                    logger.ErrorFormat(message, args);
                    break;
                case LogLevel.Fatal:
                    logger.FatalFormat(message, args);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(level), level, null);
            }
        }
        public static void Write(this ILog logger, LogLevel level, string message)
        {
            if (Defaults.MinimumLogging.Value.HasValue)
                level = level < Defaults.MinimumLogging.Value ? Defaults.MinimumLogging.Value.Value : level;

            switch (level)
            {
                case LogLevel.Debug:
                    logger.Debug(message);
                    break;
                case LogLevel.Info:
                    logger.Info(message);
                    break;
                case LogLevel.Warn:
                    logger.Warn(message);
                    break;
                case LogLevel.Error:
                    logger.Error(message);
                    break;
                case LogLevel.Fatal:
                    logger.Fatal(message);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(level), level, null);
            }
        }
        public static void Write(this ILog logger, LogLevel level, Func<string> message)
        {
            if (Defaults.MinimumLogging.Value.HasValue)
                level = level < Defaults.MinimumLogging.Value ? Defaults.MinimumLogging.Value.Value : level;
            
            switch (level)
            {
                case LogLevel.Debug:
                    logger.Debug(message());
                    break;
                case LogLevel.Info:
                    logger.Info(message());
                    break;
                case LogLevel.Warn:
                    logger.Warn(message());
                    break;
                case LogLevel.Error:
                    logger.Error(message());
                    break;
                case LogLevel.Fatal:
                    logger.Fatal(message());
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(level), level, null);
            }
        }
    }
}
