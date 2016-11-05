using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Extensions;
using Metrics;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{


    internal class EventUnitOfWork : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EventUnitOfWork));

        private static readonly Meter EventsMeter = Metric.Meter("Events", Unit.Commands);
        private static readonly Timer EventsTimer = Metric.Timer("Event Duration", Unit.Commands);

        private static readonly Meter ErrorsMeter = Metric.Meter("Event Errors", Unit.Errors);
        
        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            if (!(context.Message.Instance is IEvent))
            {
                await next().ConfigureAwait(false);
                return;
            }

            var s = new Stopwatch();
            var uows = new ConcurrentStack<IEventUnitOfWork>();
            try
            {
                EventsMeter.Mark();
                using (EventsTimer.NewContext())
                {
                    foreach (var uow in context.Builder.BuildAll<IEventUnitOfWork>())
                    {
                        uows.Push(uow);
                        uow.Builder = context.Builder;

                        var retries = 0;
                        context.Extensions.TryGet(Defaults.Attempts, out retries);
                        uow.Retries = retries;

                        await uow.Begin().ConfigureAwait(false);
                    }
                    

                    await next().ConfigureAwait(false);
                    
                    foreach (var uow in uows.Generate())
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
                Logger.Warn($"Caught exception '{e.GetType().FullName}' while executing command {context.Message.MessageType.FullName}");
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
}

