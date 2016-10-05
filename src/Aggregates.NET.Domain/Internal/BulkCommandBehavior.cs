using Aggregates.Extensions;
using Aggregates.Messages;
using Metrics;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Pipeline;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    internal class Redirect
    {
        public String Queue { get; set; }
        public Guid Instance { get; set; }
        public byte[] Mask { get; set; }
    }
    internal class ExpiringMask
    {
        public String Type { get; set; }
        public DateTime Started { get; set; }
        public byte[] Mask { get; set; }
    }

    // Using PhysicalMessageContext so we can avoid spending the time to deserialize a message we may forward elsewhere
    internal class BulkCommandBehavior :
        Behavior<IIncomingPhysicalMessageContext>
    {
        // Todo: classes, perhaps a service, this can be cleaner
        public static readonly Dictionary<String, List<Redirect>> RedirectedTypes = new Dictionary<string, List<Redirect>>();
        public static readonly Queue<ExpiringMask> Owned = new Queue<ExpiringMask>();
        public static readonly HashSet<String> DomainInstances = new HashSet<String>();

        private static readonly ILog Logger = LogManager.GetLogger(typeof(BulkCommandBehavior));

        private static Meter _redirectedMeter = Metric.Meter("Redirected Commands", Unit.Items);

        private static readonly HashSet<String> IgnoreCache = new HashSet<string>();
        private static readonly HashSet<String> CheckCache = new HashSet<string>();
        private static readonly ConcurrentDictionary<String, Queue<ExpiringMask>> ClaimCache = new ConcurrentDictionary<String, Queue<ExpiringMask>>();

        private readonly Int32 _claimThresh;
        private readonly TimeSpan _expireConflict;
        private readonly TimeSpan _claimLength;
        private readonly Decimal _commonality;
        private readonly String _endpoint;
        private readonly String _instanceSpecificQueue;

        public BulkCommandBehavior(Int32 ClaimThreshold, TimeSpan ExpireConflict, TimeSpan ClaimLength, Decimal CommonalityRequired, String Endpoint, String InstanceSpecificQueue)
        {
            _claimThresh = ClaimThreshold;
            _expireConflict = ExpireConflict;
            _claimLength = ClaimLength;
            _commonality = CommonalityRequired;
            _endpoint = Endpoint;
            _instanceSpecificQueue = InstanceSpecificQueue;
        }

        public override async Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            // if message is claim event, deserialize and add claiment to cache then stop processing

            string messageTypeIdentifier;
            if (context.MessageHeaders.TryGetValue(Headers.EnclosedMessageTypes, out messageTypeIdentifier))
            {
                var typeStr = messageTypeIdentifier.Substring(0, messageTypeIdentifier.IndexOf(';'));

                if (IgnoreCache.Contains(typeStr) ||
                    (RedirectedTypes.ContainsKey(typeStr) && !await HandleRedirectedType(typeStr, context, next)))
                {
                    await next().ConfigureAwait(false);
                    return;
                }

                var shouldCheck = CheckCache.Contains(typeStr);
                if (!shouldCheck)
                {
                    var messageType = Type.GetType(typeStr, false);
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
                    // Process next, catch ConflictCommandException
                    await next().ConfigureAwait(false);
                    return;
                }
                catch (ConflictingCommandException e)
                {
                    ex = ExceptionDispatchInfo.Capture(e);
                }

                Logger.Write(LogLevel.Debug, () => $"Caught conflicting command {typeStr} message {context.MessageId} - checking if we can CLAIM command");

                var expMask = new ExpiringMask { Type = typeStr, Started = DateTime.UtcNow, Mask = context.Message.Body };
                // Store message bytes in cache with expiry
                ClaimCache.AddOrUpdate(typeStr, (_) =>
                {
                    var queue = new Queue<ExpiringMask>();
                    queue.Enqueue(expMask);
                    return queue;
                }, (_, existing) =>
                {
                    existing.Enqueue(expMask);

                    // Remove expired saved conflicts
                    ExpiringMask removed = null;
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

                Queue<ExpiringMask> conflicts;
                ClaimCache.TryRemove(typeStr, out conflicts);
                Logger.Write(LogLevel.Info, () => $"Command {typeStr} message {context.MessageId} has been conflicted {conflicts.Count}/{_claimThresh} times - CLAIMING");

                if(Owned.Any())
                {
                    // First, take the opportunity to expire old owned masks
                    // Remove expired saved masks
                    ExpiringMask removed = null;
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
                                return context.Send<Surrender>(e =>
                                {
                                    e.Endpoint = _endpoint;
                                    e.Queue = _instanceSpecificQueue;
                                    e.Instance = Defaults.Instance;
                                    e.CommandType = removed.Type;
                                    e.Mask = removed.Mask;
                                }, options);
                            });
                        }
                    } while (removed != null);
                }

                var first = conflicts.First();
                var similarity = conflicts.Skip(1).Select(x =>
                {
                    // Jaccard Index for a simple "similarity" check
                    return (x.Mask.Intersect(first.Mask).Count() / (Decimal)x.Mask.Union(first.Mask).Count());
                }).ToList();

                Logger.Write(LogLevel.Debug, () => $"Conflicted messages are {Math.Round(similarity.Average() * 100M, 2)}% similar");
                // If not common enough, drop the one most uncommon and return (leaving the others in the cache for next conflict)
                if (similarity.Average() < _commonality)
                {
                    Logger.Warn($"Command {typeStr} message {context.MessageId} has been conflicted {conflicts.Count}/{_claimThresh} times - could not claim because conflicts are only {Math.Round(similarity.Average() * 100M, 2)}% similar we require at least {_commonality * 100M}%");

                    var trimmed = new Queue<ExpiringMask>();
                    for (var j = 0; j < similarity.Count(); j++)
                    {
                        var limbo = conflicts.Dequeue();
                        if (similarity[j] > _commonality)
                            trimmed.Enqueue(limbo);
                    }

                    ClaimCache.AddOrUpdate(typeStr, (_) => trimmed, (_, existing) =>
                    {
                        foreach (var c in existing)
                            trimmed.Enqueue(c);
                        return trimmed;
                    });
                    ex.Throw();
                }

                // Similar enough!

                // Compare all the byte streams in cache to each other to create a mask
                // A mask is something like:
                // Given messages
                //      - A blue house on the hill
                //      - A dog likes to eat
                // A mask would be
                //      - A ********s**o****
                // When deciding to forward a message to a different queue, we'll compare the message bytes to make sure the mask validates
                var mask = new MemoryStream();
                var i = 0;
                var maxLength = conflicts.Select(x => x.Mask.Count()).Min();
                while ((i + 8) < maxLength)
                {
                    var segments = conflicts.Select(x =>
                    {
                        if (x.Mask.Length < (i + 8))
                            return 0L;
                        return BitConverter.ToInt64(x.Mask, i);
                    });

                    if (segments.Skip(1).All(x => x == segments.First()))
                        mask.Write(conflicts.First().Mask, i, 8);
                    else
                        mask.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, 0, 8);

                    i += 8;
                }
                var maskArray = mask.ToArray();

                // Trim trailing FFs
                var end = maskArray.Length;
                while (maskArray[end - 1] == 0xFF)
                    end--;

                var finalMask = new byte[end];
                Array.Copy(maskArray, finalMask, finalMask.Length);
                
                if (finalMask.Length == 0)
                {
                    Logger.Warn($"Command {typeStr} has been conflicted {conflicts.Count}/{_claimThresh} times - could not claim, failed to generate mask array");
                    ex.Throw();
                }


                Logger.Write(LogLevel.Info, () => $"Claiming command {typeStr} message {context.MessageId} with mask [{Encoding.UTF8.GetString(finalMask.Select(x => (byte)(x == 0xFF ? 0x2A : x)).ToArray())}]");

                // SEND to each recorded domain instance, Publish is not ideal because the message queue could be full causing the other domains to not get Claim event for a while
                await DomainInstances.WhenAllAsync(x =>
                {
                    var options = new SendOptions();
                    options.RequireImmediateDispatch();
                    options.SetDestination(x);
                    return context.Send<Claim>(e =>
                    {
                        e.Endpoint = _endpoint;
                        e.Queue = _instanceSpecificQueue;
                        e.Instance = Defaults.Instance;
                        e.CommandType = typeStr;
                        e.Mask = finalMask;
                    }, options);
                });
            }

        }

        private async Task<Boolean> HandleRedirectedType(String type, IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            List<Redirect> redirects;
            if (!RedirectedTypes.TryGetValue(type, out redirects))
                return false;
            

            Logger.Write(LogLevel.Debug, () => $"Received possibly claimed command {type} message {context.MessageId} - checking masks");
            foreach (var redirect in redirects)
            {
                // Test mask
                var i = 0;
                while ((i + 8) < redirect.Mask.Length && i < context.Message.Body.Length)
                {
                    if ((i + 8) > context.Message.Body.Length)
                    {
                        // If we've run out of Int64's run the last few bytes individually through the mask
                        for (; i < context.Message.Body.Length; i++)
                        {
                            var m = redirect.Mask.ElementAt(i);
                            var b = context.Message.Body.ElementAt(i);
                            if ((m & b) != b)
                                break;
                        }
                    }
                    else
                    {

                        var maskInt = BitConverter.ToInt64(redirect.Mask, i);
                        var msgInt = BitConverter.ToInt64(context.Message.Body, i);

                        if ((maskInt & msgInt) != msgInt)
                            break;
                    }
                    i += 8;
                }
                // Mask matches
                Logger.Write(LogLevel.Debug, () => $"Redirecting claimed command {type} message {context.MessageId} to {redirect.Instance} - mask passed");

                _redirectedMeter.Mark();
                
                await context.ForwardCurrentMessageTo(redirect.Queue);
                return true;

            }
            // Didnt match any masks
            Logger.Write(LogLevel.Debug, () => $"Possibly claimed command {type} message {context.MessageId} - no masks matched");

            return false;
        }


    }
    internal class RedirectMessageHandler :
        IHandleMessages<Claim>,
        IHandleMessages<Surrender>,
        IHandleMessages<DomainAlive>,
        IHandleMessages<DomainDead>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(RedirectMessageHandler));


        public Task Handle(Claim message, IMessageHandlerContext context)
        {
            if (message.Instance == Defaults.Instance)
            {
                Logger.Write(LogLevel.Info, () => $"Received own claim command {message.CommandType} with mask {Encoding.UTF8.GetString(message.Mask.Select(x => (byte)(x == 0xFF ? 0x2A : x)).ToArray())}");
                BulkCommandBehavior.Owned.Enqueue(new ExpiringMask { Started = DateTime.UtcNow, Mask = message.Mask, Type = message.CommandType });
            }
            else
            {
                Logger.Write(LogLevel.Info, () => $"Received claim for type {message.CommandType} to instance {message.Instance} with mask {Encoding.UTF8.GetString(message.Mask.Select(x => (byte)(x == 0xFF ? 0x2A : x)).ToArray())}");
                if (!BulkCommandBehavior.RedirectedTypes.ContainsKey(message.CommandType))
                    BulkCommandBehavior.RedirectedTypes[message.CommandType] = new List<Redirect>();
                BulkCommandBehavior.RedirectedTypes[message.CommandType].Add(new Redirect { Queue = message.Queue, Instance = message.Instance, Mask = message.Mask });
            }

            return Task.CompletedTask;
        }

        public Task Handle(Surrender message, IMessageHandlerContext context)
        {
            if (message.Instance == Defaults.Instance)
            {
                Logger.Write(LogLevel.Debug, () => $"Surrendered claim on type {message.CommandType} with mask {Encoding.UTF8.GetString(message.Mask.Select(x => (byte)(x == 0xFF ? 0x2A : x)).ToArray())}");
            }
            else
            {
                Logger.Write(LogLevel.Info, () => $"Received surender of claim on type {message.CommandType} to instance {message.Instance} with mask {Encoding.UTF8.GetString(message.Mask.Select(x => (byte)(x == 0xFF ? 0x2A : x)).ToArray())}");
                if (!BulkCommandBehavior.RedirectedTypes.ContainsKey(message.CommandType))
                    return Task.CompletedTask;
                BulkCommandBehavior.RedirectedTypes[message.CommandType].RemoveAll(x => x.Mask.SequenceEqual(message.Mask));
                if (BulkCommandBehavior.RedirectedTypes[message.CommandType].Count == 0)
                    BulkCommandBehavior.RedirectedTypes.Remove(message.CommandType);
            }

            return Task.CompletedTask;
        }

        public Task Handle(DomainAlive message, IMessageHandlerContext context)
        {
            BulkCommandBehavior.DomainInstances.Add(message.Endpoint);
            return Task.CompletedTask;
        }

        public Task Handle(DomainDead message, IMessageHandlerContext context)
        {
            BulkCommandBehavior.DomainInstances.Remove(message.Endpoint);
            return Task.CompletedTask;
        }
    }
}
