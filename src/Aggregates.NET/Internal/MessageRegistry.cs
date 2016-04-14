using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Pipeline.Contexts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public static class MessageRegistry
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(MessageRegistry));
        [ThreadStatic]
        public static TransportMessage Current;

        public static void Add(TransportMessage message)
        {
            if (Current != null)
                Logger.WarnFormat("Registering a new transport message when one already exists..");
            Current = message;
        }
        public static void Remove()
        {
            Current = null;
        }
    }
}
