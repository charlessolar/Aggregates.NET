using NServiceBus.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Extensions
{
    public static class LoggerExtensions
    {
        public static void WriteFormat(this ILog Logger, LogLevel Level, String Message, params object[] args)
        {
            if (Defaults.MinimumLogging.Value.HasValue)
                Level = Level < Defaults.MinimumLogging.Value ? Defaults.MinimumLogging.Value.Value : Level;

            switch (Level)
            {
                case LogLevel.Debug:
                    Logger.DebugFormat(Message, args);
                    break;
                case LogLevel.Info:
                    Logger.InfoFormat(Message, args);
                    break;
                case LogLevel.Warn:
                    Logger.WarnFormat(Message, args);
                    break;
                case LogLevel.Error:
                    Logger.ErrorFormat(Message, args);
                    break;
                case LogLevel.Fatal:
                    Logger.FatalFormat(Message, args);
                    break;
            }
        }
        public static void Write(this ILog Logger, LogLevel Level, String Message)
        {
            if (Defaults.MinimumLogging.Value.HasValue)
                Level = Level < Defaults.MinimumLogging.Value ? Defaults.MinimumLogging.Value.Value : Level;

            switch (Level)
            {
                case LogLevel.Debug:
                    Logger.Debug(Message);
                    break;
                case LogLevel.Info:
                    Logger.Info(Message);
                    break;
                case LogLevel.Warn:
                    Logger.Warn(Message);
                    break;
                case LogLevel.Error:
                    Logger.Error(Message);
                    break;
                case LogLevel.Fatal:
                    Logger.Fatal(Message);
                    break;
            }
        }
    }
}
