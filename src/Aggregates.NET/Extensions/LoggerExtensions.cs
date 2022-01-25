using System;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Aggregates.Extensions
{
    [ExcludeFromCodeCoverage]
    static class LoggerExtensions
    {
        public static void LogEvent(this ILogger logger, LogLevel level, string eventId, string messageTemplate, params object[] propertyValues)
        {
            if (Defaults.MinimumLogging.Value.HasValue && level < Defaults.MinimumLogging.Value)
                level = Defaults.MinimumLogging.Value.Value;

            var props = new[] { eventId }.Concat(propertyValues).ToArray();
            logger.Log(level, "<{EventId:l}> " + messageTemplate, args: props);
        }
        public static void LogEvent(this ILogger logger, LogLevel level, string eventId, Exception ex, string messageTemplate, params object[] propertyValues)
        {
            if (Defaults.MinimumLogging.Value.HasValue && level < Defaults.MinimumLogging.Value)
                level = Defaults.MinimumLogging.Value.Value;

            var props = new[] { eventId }.Concat(propertyValues).ToArray();
            logger.Log(level, ex, "<{EventId:l}> " + messageTemplate, args: props);
        }

        public static void DebugEvent(this ILogger logger, string eventId, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Debug, eventId, messageTemplate, propertyValues);

        }
        public static void InfoEvent(this ILogger logger, string eventId, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Information, eventId, messageTemplate, propertyValues);

        }
        public static void WarnEvent(this ILogger logger, string eventId, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Warning, eventId, messageTemplate, propertyValues);

        }
        public static void ErrorEvent(this ILogger logger, string eventId, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Error, eventId, messageTemplate, propertyValues);

        }
        public static void FatalEvent(this ILogger logger, string eventId, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Critical, eventId, messageTemplate, propertyValues);

        }
        public static void DebugEvent(this ILogger logger, string eventId, Exception ex, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Debug, eventId, ex, messageTemplate, propertyValues);

        }
        public static void InfoEvent(this ILogger logger, string eventId, Exception ex, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Information, eventId, ex, messageTemplate, propertyValues);

        }
        public static void WarnEvent(this ILogger logger, string eventId, Exception ex, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Warning, eventId, ex, messageTemplate, propertyValues);

        }
        public static void ErrorEvent(this ILogger logger, string eventId, Exception ex, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Error, eventId, ex, messageTemplate, propertyValues);

        }
        public static void FatalEvent(this ILogger logger, string eventId, Exception ex, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Critical, eventId, ex, messageTemplate, propertyValues);

        }
    }
}
