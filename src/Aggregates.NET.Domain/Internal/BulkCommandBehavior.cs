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
    // Using PhysicalMessageContext so we can avoid spending the time to deserialize a message we may forward elsewhere
    internal class BulkCommandBehavior : Behavior<IIncomingPhysicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(BulkCommandBehavior));
        private static object TypeLocl = new object();
        private static Dictionary<String, Tuple<DateTime, byte[]>> OwnedDedicatedTypes = new Dictionary<string, Tuple<DateTime, byte[]>>();
        private static Dictionary<String, Tuple<Guid, byte[]>> OtherDedicatedTypes = new Dictionary<String, Tuple<Guid, byte[]>> ();
        
        private static Meter _redirectedMeter = Metric.Meter("Redirected Commands", Unit.Items);

        private static readonly HashSet<String> IgnoreCache = new HashSet<string>();
        private static readonly HashSet<String> CheckCache = new HashSet<string>();
        private static readonly ConcurrentDictionary<String, IList<Tuple<DateTime, byte[]>>> ClaimCache = new ConcurrentDictionary<String, IList<Tuple<DateTime, byte[]>>>();

        private readonly Int32 _claimThresh;
        private readonly TimeSpan _expireConflict;
        private readonly TimeSpan _claimLength;
        private readonly Decimal _commonality;
        private readonly String _localAddress;

        public BulkCommandBehavior(Int32 ClaimThreshold, TimeSpan ExpireConflict, TimeSpan ClaimLength, Decimal CommonalityRequired, String LocalAddress)
        {
            _claimThresh = ClaimThreshold;
            _expireConflict = ExpireConflict;
            _claimLength = ClaimLength;
            _commonality = CommonalityRequired;
            _localAddress = LocalAddress;
        }

        public override async Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            // if message is claim event, deserialize and add claiment to cache then stop processing

            string messageTypeIdentifier;
            if (context.MessageHeaders.TryGetValue(Headers.EnclosedMessageTypes, out messageTypeIdentifier))
            {
                var typeStr = messageTypeIdentifier.Substring(0, messageTypeIdentifier.IndexOf(';'));

                if(IgnoreCache.Contains(typeStr) ||
                    (OwnedDedicatedTypes.ContainsKey(typeStr) && !await HandleOwnedType(typeStr, context, next)) ||
                    (OtherDedicatedTypes.ContainsKey(typeStr) && !await HandleOtherType(typeStr, context, next)))
                {
                    await next().ConfigureAwait(false);
                    return;
                }

                var shouldCheck = CheckCache.Contains(typeStr);
                if (!shouldCheck)
                {
                    var messageType = Type.GetType(typeStr, false);
                    if (messageType != null && messageType.IsAssignableFrom(typeof(ICommand)))
                    {
                        shouldCheck = true;
                        CheckCache.Add(typeStr);
                    }
                    else
                    {
                        IgnoreCache.Add(typeStr);

                        await next().ConfigureAwait(false);
                        return;
                    }
                }

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
                    ex = ExceptionDispatchInfo.Capture(e.InnerException);
                }

                Logger.Write(LogLevel.Debug, () => $"Caught conflicting command {typeStr} - checking if we can CLAIM command");

                var tuple = new Tuple<DateTime, byte[]>(DateTime.UtcNow, context.Message.Body);
                // Store message bytes in cache with expiry
                ClaimCache.AddOrUpdate(typeStr, (_) =>
                {
                    return new List<Tuple<DateTime, byte[]>> { tuple };
                }, (_, existing) =>
                {
                    existing.Add(tuple);
                    // Remove expired saved conflicts
                    var expired = existing.Where(x => (DateTime.UtcNow - x.Item1) > _expireConflict).ToList();
                    expired.ForEach(x => existing.Remove(x));

                    return existing;
                });

                // If we see more than X exceptions
                Logger.Write(LogLevel.Debug, () => $"Command {typeStr} has been conflicted {ClaimCache[typeStr].Count}/{_claimThresh} times");
                if (ClaimCache[typeStr].Count < _claimThresh)
                    ex.Throw();

                IList<Tuple<DateTime, byte[]>> conflicts;
                ClaimCache.TryRemove(typeStr, out conflicts);
                Logger.Write(LogLevel.Info, () => $"Command {typeStr} has been conflicted {conflicts.Count}/{_claimThresh} times - CLAIMING");

                // Compare all the byte streams in cache to each other to create a mask
                // Mask will be SequenceEquals to all byte streams in cache
                var mask = new MemoryStream();
                var i = 0;
                var maxLength = conflicts.Select(x => x.Item2.Count()).Min();
                var incommon = 0;
                while ((i + 8) < maxLength)
                {
                    var segments = conflicts.Select(x =>
                    {
                        if (x.Item2.Length < (i + 8))
                            return 0L;
                        return BitConverter.ToInt64(x.Item2, i);
                    });

                    if (segments.Skip(1).All(x => x == segments.First()))
                    {
                        incommon++;
                        mask.Write(conflicts.First().Item2, i, 8);
                    }
                    else
                        mask.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, 0, 8);

                    i += 8;
                }
                Logger.Write(LogLevel.Debug, () => $"Finished scanning conflicts, messages are {Math.Floor(incommon / (maxLength / 8M)) * 100M}% similar");

                // Streams must have specified commonality
                if((incommon / (maxLength / 8)) < _commonality)
                {
                    Logger.Warn($"Command {typeStr} has been conflicted {conflicts.Count}/{_claimThresh} times - could not claim because conflicts are only {Math.Floor(incommon / (maxLength / 8M)) * 100M}% similar we require at least {_commonality * 100M}%");
                    ex.Throw();
                }

                var maskArray = mask.ToArray();
                if(maskArray.Length == 0)
                {
                    Logger.Warn($"Command {typeStr} has been conflicted {conflicts.Count}/{_claimThresh} times - could not claim, failed to generate mask array");
                    ex.Throw();
                }


                Logger.Write(LogLevel.Info, () => $"Claiming command {typeStr} with mask {Encoding.UTF8.GetString(maskArray.Select(x => (byte)(x == 0xFF ? 0x20 : x)).ToArray())}");
                OwnedDedicatedTypes[typeStr] = new Tuple<DateTime, byte[]>(DateTime.UtcNow, maskArray);
                // Publish CLAIM event with byte mask and command type
                var options = new PublishOptions();
                options.RequireImmediateDispatch();
                await context.Publish<ClaimCommand>(x =>
                {
                    x.Instance = Defaults.Instance;
                    x.CommandType = typeStr;
                    x.Mask = maskArray;
                });
            }

        }


        private async Task<Boolean> HandleOwnedType(String type, IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            Tuple<DateTime, byte[]> mask;
            if (!OwnedDedicatedTypes.TryGetValue(type, out mask))
                return false;

            if((DateTime.UtcNow - mask.Item1) > _claimLength)
            {
                Logger.Write(LogLevel.Info, () => $"Claimed command {type} with mask {Encoding.UTF8.GetString(mask.Item2.Select(x => (byte)(x == 0xFF ? 0x20 : x)).ToArray())} has expired");
                var options = new PublishOptions();
                options.RequireImmediateDispatch();
                await context.Publish<SurrenderCommand>(x =>
                {
                    x.Instance = Defaults.Instance;
                    x.CommandType = type;
                    x.Mask = mask.Item2;
                });
                OwnedDedicatedTypes.Remove(type);
                return false;
            }

            await next().ConfigureAwait(false);
            return true;
        }
        private async Task<Boolean> HandleOtherType(String type, IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            Tuple<Guid, byte[]> mask;
            if (!OtherDedicatedTypes.TryGetValue(type, out mask))
                return false;

            Logger.Write(LogLevel.Debug, () => $"Received possibly claimed command {type} - checking mask");
            // Test mask
            var i = 0;
            while ((i + 8) < mask.Item2.Length)
            {
                if ((i + 8) < context.Message.Body.Length)
                {
                    // If we've run out of Int64's run the last few bytes individually through the mask
                    for (; i < context.Message.Body.Length; i++)
                    {
                        var m = mask.Item2.ElementAt(i);
                        var b = context.Message.Body.ElementAt(i);
                        if ((m & b) != b)
                            return false;
                    }
                }
                else
                {

                    var maskInt = BitConverter.ToInt64(mask.Item2, i);
                    var msgInt = BitConverter.ToInt64(context.Message.Body, i);

                    if ((maskInt & msgInt) != msgInt)
                        return false;
                }
                i += 8;
            }

            // Test successfull!
            Logger.Write(LogLevel.Debug, () => $"Redirecting claimed command {type} to {mask.Item1} - mask passed");

            _redirectedMeter.Mark();

            // Dont like how I have to hack together the instance specific queue, would be nice if ForwardCurrentMessage took SendOptions
            await context.ForwardCurrentMessageTo($"{_localAddress}-{mask.Item1}");
            return true;
        }
    }
}
