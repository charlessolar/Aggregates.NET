using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Logging;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    internal class UnitOfWorkExecutor : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogProvider.GetLogger("UOW Executor");

        private readonly IMetrics _metrics;

        public UnitOfWorkExecutor(IMetrics metrics)
        {
            _metrics = metrics;
        }

        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            var container = Configuration.Settings.Container;

            // Child container with resolved domain and app uow used by downstream
            var child = container.GetChildContainer();
            context.Extensions.Set(child);

            // Only SEND messages deserve a UnitOfWork
            if (context.MessageHeaders[Headers.MessageIntent] != MessageIntentEnum.Send.ToString() && context.MessageHeaders[Headers.MessageIntent] != MessageIntentEnum.Publish.ToString())
            {
                await next().ConfigureAwait(false);
                return;
            }
            if(!typeof(Messages.ICommand).IsAssignableFrom(context.Message.MessageType) && !typeof(Messages.IEvent).IsAssignableFrom(context.Message.MessageType))
            {
                Logger.Write(LogLevel.Debug, "Message is not an ICommand nor IEvent, skipping UnitOfWork");

                await next().ConfigureAwait(false);
                return;
            }
            if(context.Message.MessageType == typeof(Messages.Accept) || context.Message.MessageType == typeof(Messages.Reject))
            {
                // If this happens the callback for the message took too long (likely due to a timeout)
                // normall NSB will report an exception for "No Handlers" - this will just log a warning and ignore
                Logger.Write(LogLevel.Warn, $"Received overdue {context.Message.MessageType.Name} callback - your timeouts might be too short");
                return;
            }
            
            var domainUOW = child.Resolve<IDomainUnitOfWork>();
            var delayed = child.Resolve<IDelayedChannel>();
            IUnitOfWork appUOW = null;
            try
            {
                // IUnitOfWork might not be defined by user
                appUOW = child.Resolve<IUnitOfWork>();
            }
            catch { }


            Logger.Write(LogLevel.Debug,
                () => $"Starting UOW for message {context.MessageId} type {context.Message.MessageType.FullName}");

            try
            {
                _metrics.Increment("Messages Concurrent", Unit.Message);
                _metrics.Mark("Messages", Unit.Message);
                using (_metrics.Begin("Message Duration"))
                {

                    Logger.Write(LogLevel.Debug, () => $"Running UOW.Begin for message {context.MessageId}");
                    await domainUOW.Begin().ConfigureAwait(false);
                    if (appUOW != null)
                        await appUOW.Begin().ConfigureAwait(false);
                    await delayed.Begin().ConfigureAwait(false);


                    // Todo: because commit id is used on commit now instead of during processing we can
                    // once again parallelize event processing (if we want to)

                    // Stupid hack to get events from ES and messages from NSB into the same pipeline
                    IDelayedMessage[] delayedMessages;
                    object @event;
                    // Special case for delayed messages read from delayed stream
                    if (context.Extensions.TryGet(Defaults.LocalBulkHeader, out delayedMessages))
                    {
                        Logger.Write(LogLevel.Debug, () => $"Bulk processing {delayedMessages.Count()} messages, bulk id {context.MessageId}");
                        var index = 1;
                        foreach (var x in delayedMessages)
                        {
                            // Replace all headers with the original headers to preserve CorrId etc.
                            context.Headers.Clear();
                            foreach (var header in x.Headers)
                                context.Headers[$"{Defaults.DelayedPrefixHeader}.{header.Key}"] = header.Value;

                            context.Headers[Defaults.LocalBulkHeader] = delayedMessages.Count().ToString();
                            context.Headers[Defaults.DelayedId] = x.MessageId;
                            context.Headers[Defaults.ChannelKey] = x.ChannelKey;
                            Logger.Write(LogLevel.Debug, () => $"Processing {index}/{delayedMessages.Count()} message, bulk id {context.MessageId}.  MessageId: {x.MessageId} ChannelKey: {x.ChannelKey}");

                            //context.Extensions.Set(Defaults.ChannelKey, x.ChannelKey);

                            context.UpdateMessageInstance(x.Message);
                            await next().ConfigureAwait(false);
                            index++;
                        }
                    }
                    else if (context.Extensions.TryGet(Defaults.LocalHeader, out @event))
                    {
                        context.UpdateMessageInstance(@event);
                        await next().ConfigureAwait(false);
                    }
                    else
                        await next().ConfigureAwait(false);


                    Logger.Write(LogLevel.Debug, () => $"Running UOW.End for message {context.MessageId}");

                    await domainUOW.End().ConfigureAwait(false);
                    if (appUOW != null)
                        await appUOW.End().ConfigureAwait(false);
                    await delayed.End().ConfigureAwait(false);
                }

            }
            catch (Exception e)
            {
                Logger.Info($"Caught exception '{e.GetType().FullName}' while executing message {context.MessageId} {context.Message.MessageType.FullName}", e);

                _metrics.Mark("Message Errors", Unit.Errors);
                var trailingExceptions = new List<Exception>();

                try
                {
                    Logger.Write(LogLevel.Debug,
                        () => $"Running UOW.End with exception [{e.GetType().Name}] for message {context.MessageId}");
                    // Todo: if one throws an exception (again) the others wont work.  Fix with a loop of some kind
                    await domainUOW.End(e).ConfigureAwait(false);
                    if (appUOW != null)
                        await appUOW.End(e).ConfigureAwait(false);
                    await delayed.End(e).ConfigureAwait(false);
                }
                catch (Exception endException)
                {
                    trailingExceptions.Add(endException);
                }


                if (trailingExceptions.Any())
                {
                    trailingExceptions.Insert(0, e);
                    throw new System.AggregateException(trailingExceptions);
                }
                throw;

            }
            finally
            {
                child.Dispose();
                _metrics.Decrement("Messages Concurrent", Unit.Message);
                context.Extensions.Remove<IContainer>();
            }
        }
    }
    internal class UowRegistration : RegisterStep
    {
        public UowRegistration() : base(
            stepId: "UnitOfWorkExecution",
            behavior: typeof(UnitOfWorkExecutor),
            description: "Begins and Ends unit of work for your application"
        )
        {
            InsertAfterIfExists("ExecuteUnitOfWork");
        }
    }
}

