using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Aggregates.Logging;
using Aggregates.Extensions;

namespace Aggregates.Internal
{
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
            Logger.Write(getLogLevel(level), ex == null ? message : $"{message}\n{ex.AsString()}");
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
