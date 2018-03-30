using System;
using Aggregates.Logging;
using System.Linq;

namespace Aggregates.Extensions
{
    static class LoggerExtensions
    {
        public static void LogEvent(this ILog logger, LogLevel level, string eventId, string messageTemplate, params object[] propertyValues)
        {
            if (Defaults.MinimumLogging.Value.HasValue && level < Defaults.MinimumLogging.Value)
                level = Defaults.MinimumLogging.Value.Value;

            var props = new[] { eventId }.Concat(propertyValues).ToArray();
            logger.Log(level, () => "<{EventId:l}> " + messageTemplate, formatParameters: props);
        }
        public static void LogEvent(this ILog logger, LogLevel level, string eventId, Exception ex, string messageTemplate, params object[] propertyValues)
        {
            if (Defaults.MinimumLogging.Value.HasValue && level < Defaults.MinimumLogging.Value)
                level = Defaults.MinimumLogging.Value.Value;

            var props = new[] { eventId }.Concat(propertyValues).ToArray();
            logger.Log(level, () => "<{EventId:l}> " + messageTemplate, exception: ex, formatParameters: props);
        }

        public static void DebugEvent(this ILog logger, string eventId, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Debug, eventId, messageTemplate, propertyValues);

        }
        public static void InfoEvent(this ILog logger, string eventId, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Info, eventId, messageTemplate, propertyValues);

        }
        public static void WarnEvent(this ILog logger, string eventId, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Warn, eventId, messageTemplate, propertyValues);

        }
        public static void ErrorEvent(this ILog logger, string eventId, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Error, eventId, messageTemplate, propertyValues);

        }
        public static void FatalEvent(this ILog logger, string eventId, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Fatal, eventId, messageTemplate, propertyValues);

        }
        public static void DebugEvent(this ILog logger, string eventId, Exception ex, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Debug, eventId, ex, messageTemplate, propertyValues);

        }
        public static void InfoEvent(this ILog logger, string eventId, Exception ex, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Info, eventId, ex, messageTemplate, propertyValues);

        }
        public static void WarnEvent(this ILog logger, string eventId, Exception ex, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Warn, eventId, ex, messageTemplate, propertyValues);

        }
        public static void ErrorEvent(this ILog logger, string eventId, Exception ex, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Error, eventId, ex, messageTemplate, propertyValues);

        }
        public static void FatalEvent(this ILog logger, string eventId, Exception ex, string messageTemplate, params object[] propertyValues)
        {
            logger.LogEvent(LogLevel.Fatal, eventId, ex, messageTemplate, propertyValues);

        }
    }
}
