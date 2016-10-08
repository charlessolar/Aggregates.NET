using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Messages;
using Metrics;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    internal class Redirect
    {
        public string Queue { get; set; }
        public Guid Instance { get; set; }
    }
    internal class ExpiringClaim
    {
        public string Type { get; set; }
        public DateTime Started { get; set; }
    }

    // Using PhysicalMessageContext so we can avoid spending the time to deserialize a message we may forward elsewhere
    internal class BulkCommandBehavior :
        Behavior<IIncomingPhysicalMessageContext>
    {
        // Todo: classes, perhaps a service, this can be cleaner
        public static readonly Dictionary<string, List<Redirect>> RedirectedTypes = new Dictionary<string, List<Redirect>>();
        public static readonly Queue<ExpiringClaim> Owned = new Queue<ExpiringClaim>();
        public static readonly HashSet<string> DomainInstances = new HashSet<string>();
        public static readonly ConcurrentDictionary<string, Queue<ExpiringClaim>> ClaimCache = new ConcurrentDictionary<string, Queue<ExpiringClaim>>();

        private static readonly ILog Logger = LogManager.GetLogger(typeof(BulkCommandBehavior));

        private static readonly Meter RedirectedMeter = Metric.Meter("Redirected Commands", Unit.Items);

        private static readonly HashSet<string> IgnoreCache = new HashSet<string>();
        private static readonly HashSet<string> CheckCache = new HashSet<string>();

        private readonly int _claimThresh;
        private readonly TimeSpan _expireConflict;
        private readonly TimeSpan _claimLength;
        private readonly decimal _commonality;
        private readonly string _endpoint;
        private readonly string _instanceSpecificQueue;

        public BulkCommandBehavior(int claimThreshold, TimeSpan expireConflict, TimeSpan claimLength, decimal commonalityRequired, string endpoint, string instanceSpecificQueue)
        {
            _claimThresh = claimThreshold;
            _expireConflict = expireConflict;
            _claimLength = claimLength;
            _commonality = commonalityRequired;
            _endpoint = endpoint;
            _instanceSpecificQueue = instanceSpecificQueue;
        }

        public override async Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            // if message is claim event, deserialize and add claiment to cache then stop processing

            string messageTypeIdentifier;
            if (!context.MessageHeaders.TryGetValue(Headers.EnclosedMessageTypes, out messageTypeIdentifier))
            {
                await next().ConfigureAwait(false);
                return;
            }
            var typeStr = messageTypeIdentifier.Substring(0, messageTypeIdentifier.IndexOf(','));

            if (IgnoreCache.Contains(typeStr))
            {
                await next().ConfigureAwait(false);
                return;
            }
            if (RedirectedTypes.ContainsKey(typeStr))
            {
                if (await HandleRedirectedType(typeStr, context))
                    return;

                await next().ConfigureAwait(false);
                return;
            }

            var shouldCheck = CheckCache.Contains(typeStr);
            if (!shouldCheck)
            {
                // Use the full assembly name when Type.GetType
                var messageType = Type.GetType(messageTypeIdentifier.Substring(0, messageTypeIdentifier.IndexOf(';')), false);
                if (messageType != null && typeof(ICommand).IsAssignableFrom(messageType))
                {
                    Logger.Write(LogLevel.Debug, () => $"Message type {typeStr} message {context.MessageId} detected as a command - will watch for conflicting command exceptions");
                    shouldCheck = true;
                    CheckCache.Add(typeStr);
                }
                else
                {
                    Logger.Write(LogLevel.Debug, () => $"Message type {typeStr} message {context.MessageId} is not a command - ignoring conflicting command exceptions");
                    IgnoreCache.Add(typeStr);

                    await next().ConfigureAwait(false);
                    return;
                }
            }

            Logger.Write(LogLevel.Debug, () => $"Command {typeStr} message {context.MessageId} received - watching for conflicting command exceptions");
            // Do a check if we should claim this command
            ExceptionDispatchInfo ex;
            try
            {
                // Process next, catch ConflictResolutionFailedException
                await next().ConfigureAwait(false);
                return;
            }
            catch (ConflictResolutionFailedException e)
            {
                ex = ExceptionDispatchInfo.Capture(e);
            }

            Logger.Write(LogLevel.Debug, () => $"Caught conflicting command {typeStr} message {context.MessageId} - checking if we can CLAIM command");

            var expMask = new ExpiringClaim { Type = typeStr, Started = DateTime.UtcNow };
            // Store message bytes in cache with expiry
            ClaimCache.AddOrUpdate(typeStr, _ =>
            {
                var queue = new Queue<ExpiringClaim>();
                queue.Enqueue(expMask);
                return queue;
            }, (_, existing) =>
            {
                existing.Enqueue(expMask);

                // Remove expired saved conflicts
                ExpiringClaim removed = null;
                do
                {
                    var top = existing.Peek();
                    if ((DateTime.UtcNow - top.Started) > _expireConflict)
                        removed = existing.Dequeue();
                } while (removed != null);

                return existing;
            });

            // If we see more than X exceptions
            Logger.Write(LogLevel.Debug, () => $"Command {typeStr} message {context.MessageId} has been conflicted {ClaimCache[typeStr].Count}/{_claimThresh} times");
            if (ClaimCache[typeStr].Count < _claimThresh)
                ex.Throw();

            Queue<ExpiringClaim> conflicts;
            ClaimCache.TryRemove(typeStr, out conflicts);
            Logger.Write(LogLevel.Info, () => $"Command {typeStr} message {context.MessageId} has been conflicted {conflicts.Count}/{_claimThresh} times - CLAIMING");

            if (Owned.Any())
            {
                // First, take the opportunity to expire old owned masks
                // Remove expired saved masks
                ExpiringClaim removed = null;
                do
                {
                    var top = Owned.Peek();
                    if ((DateTime.UtcNow - top.Started) > _expireConflict)
                    {
                        removed = Owned.Dequeue();
                        await DomainInstances.WhenAllAsync(x =>
                        {
                            var options = new SendOptions();
                            options.RequireImmediateDispatch();
                            options.SetDestination(x);
                            return context.Send<ISurrender>(e =>
                            {
                                e.Endpoint = _endpoint;
                                e.Queue = _instanceSpecificQueue;
                                e.Instance = Defaults.Instance;
                                e.CommandType = removed.Type;
                            }, options);
                        });
                    }
                } while (removed != null);
            }

            Logger.Write(LogLevel.Info, () => $"Claiming command {typeStr} message {context.MessageId}");

            // SEND to each recorded domain instance, Publish is not ideal because the message queue could be full causing the other domains to not get Claim event for a while
            await DomainInstances.WhenAllAsync(x =>
            {
                var options = new SendOptions();
                options.RequireImmediateDispatch();
                options.SetDestination(x);
                return context.Send<IClaim>(e =>
                {
                    e.Endpoint = _endpoint;
                    e.Queue = _instanceSpecificQueue;
                    e.Instance = Defaults.Instance;
                    e.CommandType = typeStr;
                }, options);
            });
        }

        private static async Task<bool> HandleRedirectedType(string type, IIncomingPhysicalMessageContext context)
        {
            List<Redirect> redirects;
            if (!RedirectedTypes.TryGetValue(type, out redirects))
                return false;

            var redirect = redirects.First();

            Logger.Write(LogLevel.Info, () => $"Redirecting claimed command {type} message {context.MessageId} to {redirect.Instance}");

            RedirectedMeter.Mark();

            await context.ForwardCurrentMessageTo(redirect.Queue);
            return true;
        }


    }
    // Todo: if we are using RabbitMq we can actually change the routing topology of senders / receivers 
    internal class RedirectMessageHandler :
        IHandleMessages<IClaim>,
        IHandleMessages<ISurrender>,
        IHandleMessages<IDomainAlive>,
        IHandleMessages<IDomainDead>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(RedirectMessageHandler));


        public Task Handle(IClaim message, IMessageHandlerContext context)
        {
            if (message.Instance == Defaults.Instance)
            {
                Logger.Write(LogLevel.Info, () => $"Received own claim for command {message.CommandType}");
                BulkCommandBehavior.Owned.Enqueue(new ExpiringClaim { Started = DateTime.UtcNow, Type = message.CommandType });
            }
            else
            {
                Logger.Write(LogLevel.Info, () => $"Received claim for commain {message.CommandType} to instance {message.Instance}");

                Queue<ExpiringClaim> temp;
                BulkCommandBehavior.ClaimCache.TryRemove(message.CommandType, out temp);
                if (!BulkCommandBehavior.RedirectedTypes.ContainsKey(message.CommandType))
                    BulkCommandBehavior.RedirectedTypes[message.CommandType] = new List<Redirect>();
                BulkCommandBehavior.RedirectedTypes[message.CommandType].Add(new Redirect { Queue = message.Queue, Instance = message.Instance });
            }

            return Task.CompletedTask;
        }

        public Task Handle(ISurrender message, IMessageHandlerContext context)
        {
            if (message.Instance == Defaults.Instance)
            {
                Logger.Write(LogLevel.Debug, () => $"Surrendered claim on command {message.CommandType}");
            }
            else
            {
                Logger.Write(LogLevel.Info, () => $"Received surender of claim on type {message.CommandType} to instance {message.Instance}");
                if (!BulkCommandBehavior.RedirectedTypes.ContainsKey(message.CommandType))
                    return Task.CompletedTask;
                BulkCommandBehavior.RedirectedTypes[message.CommandType].RemoveAll(x => x.Instance == message.Instance);
                if (BulkCommandBehavior.RedirectedTypes[message.CommandType].Count == 0)
                    BulkCommandBehavior.RedirectedTypes.Remove(message.CommandType);
            }

            return Task.CompletedTask;
        }

        public Task Handle(IDomainAlive message, IMessageHandlerContext context)
        {
            BulkCommandBehavior.DomainInstances.Add(message.Endpoint);
            return Task.CompletedTask;
        }

        public Task Handle(IDomainDead message, IMessageHandlerContext context)
        {
            BulkCommandBehavior.DomainInstances.Remove(message.Endpoint);
            return Task.CompletedTask;
        }
    }
}
