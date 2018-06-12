using Newtonsoft.Json.Serialization;
using System;
using Aggregates.Logging;
using Aggregates.Extensions;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class TraceWriter : ITraceWriter
    {
        private static readonly ILog Logger = LogProvider.GetLogger("Json");

        public TraceLevel LevelFilter
        {
            get
            {
                if (Logger.IsDebugEnabled())
                    return TraceLevel.Verbose;
                if (Logger.IsInfoEnabled())
                    return TraceLevel.Info;
                if (Logger.IsWarnEnabled())
                    return TraceLevel.Warning;
                if (Logger.IsErrorEnabled())
                    return TraceLevel.Error;

                return TraceLevel.Off;
            }
        }

        public void Trace(TraceLevel level, string message, Exception ex)
        {
            Logger.LogEvent(getLogLevel(level), "Trace", ex, message);
        }

        private LogLevel getLogLevel(TraceLevel level)
        {
            switch (level)
            {
                case TraceLevel.Verbose:
                    return LogLevel.Debug;
                case TraceLevel.Info:
                    return LogLevel.Info;
                case TraceLevel.Warning:
                    return LogLevel.Warn;
                case TraceLevel.Error:
                    return LogLevel.Error;
                default:
                    return LogLevel.Info;
            }
        }
    }
}
