using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using NServiceBus.Extensibility;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Internal
{
    class ExpiringBulkInvokes : IApplicationUnitOfWork
    {
        private static readonly ILog Logger = LogManager.GetLogger("ExpiringBulkInvokes");

        // Todo: this is a terrible structure but the use of this should be pretty limited.  Profiling results needed
        private static readonly object Lock = new object();
        private static readonly Dictionary<string, Dictionary<string, DateTime>> DelayedExpirations = new Dictionary<string, Dictionary<string, DateTime>>();
        

        public static void Add(string handlerKey, string channelKey, TimeSpan expires)
        {
            lock (Lock)
            {
                if (!DelayedExpirations.ContainsKey(handlerKey))
                {
                    DelayedExpirations[handlerKey] = new Dictionary<string, DateTime>
                            {
                                {channelKey, DateTime.UtcNow + expires}
                            };
                    return;
                }
                if (DelayedExpirations[handlerKey].ContainsKey(channelKey))
                    return;
                DelayedExpirations[handlerKey].Add(channelKey, DateTime.UtcNow + expires);
            }
        }

        public static void Remove(string handlerKey, string channelKey)
        {
            lock (Lock)
            {
                if (!DelayedExpirations.ContainsKey(handlerKey))
                    return;
                DelayedExpirations[handlerKey].Remove(channelKey);
            }
            
        }

        private static readonly object CheckLock = new object();
        private static DateTime _lastCheck = DateTime.UtcNow;
        
        public IBuilder Builder { get; set; }
        public int Retries { get; set; }
        public ContextBag Bag { get; set; }

        public Task Begin()
        {
            return Task.CompletedTask;
        }

        public async Task End(Exception ex = null)
        {
            lock (CheckLock)
            {
                if ((DateTime.UtcNow - _lastCheck).TotalSeconds < 10)
                    return;

                _lastCheck = DateTime.UtcNow;
            }
            Logger.Write(LogLevel.Debug, () => $"Checking for expired channels");

            var channel = Builder.Build<IDelayedChannel>();
            var expired = new List<Tuple<string, string>>();
            lock (Lock)
            {
                foreach (var kv in DelayedExpirations)
                {
                    foreach (var e in kv.Value.Where(x => x.Value < DateTime.UtcNow).ToList())
                    {
                        kv.Value.Remove(e.Key);
                        expired.Add(new Tuple<string, string>(kv.Key, e.Key));
                    }
                }
            }
            foreach (var e in expired)
            {
                Logger.Write(LogLevel.Debug, () => $"Found expired channel {e.Item2}");
                await channel.AddToQueue(e.Item1, e.Item2).ConfigureAwait(false);
            }

        }
    }
}
