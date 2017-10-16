using Aggregates.Logging;
using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Extensions;

namespace Aggregates
{
    public static class MessageExtensions
    {
        private static readonly ILog Logger = LogProvider.GetLogger("Response");

        public static void CommandResponse(this IMessage msg)
        {
            // ReSharper disable once CanBeReplacedWithTryCastAndCheckForNull
            if (msg is Reject)
            {
                var reject = (Reject)msg;
                Logger.WriteFormat(LogLevel.Warn, "Command was rejected - Message: {0}\n", reject.Message);
                throw new CommandRejectedException(reject.Message, reject.Exception);
            }
            // ReSharper disable once CanBeReplacedWithTryCastAndCheckForNull
            if (msg is Error)
            {
                var error = (Error)msg;
                Logger.Warn($"Command Fault!\n{error.Message}");
                throw new CommandRejectedException($"Command Fault!\n{error.Message}");
            }
        }
    }
}
