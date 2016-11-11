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
        private static readonly ILog Logger = LogManager.GetLogger(typeof(ApplicationUnitOfWork));

        private static readonly Meter EventsMeter = Metric.Meter("Messages", Unit.Items);
        private static readonly Timer EventsTimer = Metric.Timer("Message Duration", Unit.Items);

        private static readonly Meter ErrorsMeter = Metric.Meter("Message Errors", Unit.Errors);

        private readonly IPersistence _persistence;

        public ApplicationUnitOfWork(IPersistence persistence)
        {
            _persistence = persistence;
        }
        
        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            var s = new Stopwatch();
            var uows = new ConcurrentStack<IApplicationUnitOfWork>();
            try
            {
                EventsMeter.Mark();
                using (EventsTimer.NewContext())
                {
                    foreach (var uow in context.Builder.BuildAll<IApplicationUnitOfWork>())
                    {
                        uows.Push(uow);
                        uow.Builder = context.Builder;

                        int retries;
                        if (!context.Extensions.TryGet(Defaults.Retries, out retries))
                            retries = 0;
                        uow.Retries = retries;

                        var savedBag =
                            await _persistence.Remove($"{context.MessageId}-{uow.GetType().FullName}")
                                    .ConfigureAwait(false);

                        uow.Bag = savedBag ?? new ContextBag();

                        await uow.Begin().ConfigureAwait(false);
                    }
                    

                    await next().ConfigureAwait(false);
                    
                    // Order commits by ones that can fail
                    foreach (var uow in uows.Generate().OrderByDescending(x => x.CanFail))
                    {
                        try
                        {
                            await uow.End().ConfigureAwait(false);
                        }
                        catch
                        {
                            // If it failed it needs to go back on the stack
                            uows.Push(uow);
                            throw;
                        }
                    }

                }
            }
            catch (Exception e)
            {
                Logger.Warn($"Caught exception '{e.GetType().FullName}' while executing message {context.Message.MessageType.FullName}");
                ErrorsMeter.Mark();
                var trailingExceptions = new List<Exception>();
                foreach (var uow in uows.Generate())
                {
                    try
                    {
                        await uow.End(e).ConfigureAwait(false);
                    }
                    catch (Exception endException)
                    {
                        trailingExceptions.Add(endException);
                    }
                    await _persistence.Save($"{context.MessageId}-{uow.GetType().FullName}", uow.Bag).ConfigureAwait(false);
                }


                if (trailingExceptions.Any())
                {
                    trailingExceptions.Insert(0, e);
                    e = new System.AggregateException(trailingExceptions);
                }
                throw;
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
        }
    }
}

