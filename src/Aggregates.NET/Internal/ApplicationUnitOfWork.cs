using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Metrics;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Logging;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    internal class ApplicationUnitOfWork : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger("ApplicationUnitOfWork");

        private static readonly Meter MessagesMeter = Metric.Meter("Messages", Unit.Items);
        private static readonly Metrics.Timer MessagesTimer = Metric.Timer("Message Duration", Unit.Items);
        private static readonly Metrics.Timer BeginTimer = Metric.Timer("UOW Begin Duration", Unit.Items, tags: "debug");
        private static readonly Metrics.Timer ProcessTimer = Metric.Timer("UOW Process Duration", Unit.Items, tags: "debug");
        private static readonly Metrics.Timer EndTimer = Metric.Timer("UOW End Duration", Unit.Items, tags: "debug");
        private static readonly Counter MessagesConcurrent = Metric.Counter("Messages Concurrent", Unit.Items);

        private static readonly Meter ErrorsMeter = Metric.Meter("UOW Errors", Unit.Errors);

        private readonly IPersistence _persistence;

        public ApplicationUnitOfWork(IPersistence persistence)
        {
            _persistence = persistence;
        }

        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            MessagesConcurrent.Increment();

            // Only SEND messages deserve a UnitOfWork
            if (context.MessageHeaders[Headers.MessageIntent] != MessageIntentEnum.Send.ToString())
            {
                await next().ConfigureAwait(false);
                return;
            }


                Logger.Write(LogLevel.Info,
                () => $"Starting UOW for message {context.MessageId} type {context.Message.MessageType.FullName}");
            var uows = new Stack<IApplicationUnitOfWork>();
            try
            {
                MessagesMeter.Mark();
                using (MessagesTimer.NewContext())
                {
                    using (BeginTimer.NewContext())
                    {
                        var listOfUows = context.Builder.BuildAll<IApplicationUnitOfWork>();
                        // Trick to put ILastApplicationUnitOfWork at the bottom of the stack to be uow.End'd last
                        foreach (var uow in listOfUows.Where(x => x is ILastApplicationUnitOfWork).Concat(listOfUows.Where(x => !(x is ILastApplicationUnitOfWork))))
                        {
                            uow.Builder = context.Builder;

                            int retries;
                            if (!context.Extensions.TryGet(Defaults.Retries, out retries))
                                retries = 0;
                            uow.Retries = retries;

                            var savedBag =
                                await _persistence.Remove(context.MessageId, uow.GetType()).ConfigureAwait(false);
                            
                            uow.Bag = savedBag ?? new ContextBag();
                            Logger.Write(LogLevel.Debug, () => $"Running UOW.Begin for message {context.MessageId} on {uow.GetType().FullName}");
                            await uow.Begin().ConfigureAwait(false);
                            uows.Push(uow);
                        }
                    }

                    using (ProcessTimer.NewContext())
                    {
                        // Special case for delayed messages read from delayed stream
                        if (context.Headers.ContainsKey(Defaults.BulkHeader))
                        {
                            DelayedMessage[] delayed;
                            if (!context.Extensions.TryGet(Defaults.BulkHeader, out delayed))
                                await next().ConfigureAwait(false);

                            foreach (var x in delayed)
                            {
                                // Todo: should we overwrite the headers for the message with the delayed ones?
                                // dont really see the point yet
                                context.Headers[Defaults.ChannelKey] = x.ChannelKey;
                                context.UpdateMessageInstance(x.Message);
                                await next().ConfigureAwait(false);
                            }

                        }
                        else
                            await next().ConfigureAwait(false);
                    }

                    using (EndTimer.NewContext())
                    {
                        foreach (var uow in uows.PopAll())
                        {
                            Logger.Write(LogLevel.Debug, () => $"Running UOW.End for message {context.MessageId} on {uow.GetType().FullName}");
                            
                            try
                            {
                                // ConfigureAwait true because we don't want uow.End running in parrallel
                                await uow.End().ConfigureAwait(true);
                            }
                            finally
                            {
                                await _persistence.Save(context.MessageId, uow.GetType(), uow.Bag).ConfigureAwait(true);
                            }
                        }
                    }
                    // Only clear context bags once all UOWs complete successfully
                    await _persistence.Clear(context.MessageId).ConfigureAwait(false);
                }

            }
            catch (Exception e)
            {
                Logger.Warn(
                    $"Caught exception '{e.GetType().FullName}' while executing message {context.MessageId} {context.Message.MessageType.FullName}");

                ErrorsMeter.Mark(context.Message.MessageType.FullName);
                var trailingExceptions = new List<Exception>();
                using (EndTimer.NewContext())
                {
                    foreach (var uow in uows.PopAll())
                    {
                        try
                        {
                            Logger.Write(LogLevel.Debug,
                                () => $"Running UOW.End with exception [{e.GetType().Name}] for message {context.MessageId} on {uow.GetType().FullName}");
                            await uow.End(e).ConfigureAwait(true);
                        }
                        catch (Exception endException)
                        {
                            trailingExceptions.Add(endException);
                        }
                        await _persistence.Save(context.MessageId, uow.GetType(), uow.Bag).ConfigureAwait(true);
                    }
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
                MessagesConcurrent.Decrement();
            }
        }
    }
    internal class ApplicationUowRegistration : RegisterStep
    {
        public ApplicationUowRegistration() : base(
            stepId: "ApplicationUnitOfWork",
            behavior: typeof(ApplicationUnitOfWork),
            description: "Begins and Ends unit of work for your application"
        )
        {
            InsertAfterIfExists("ExecuteUnitOfWork");
        }
    }
}

