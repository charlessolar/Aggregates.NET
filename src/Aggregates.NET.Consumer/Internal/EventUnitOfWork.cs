using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Extensions;
using Metrics;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{


    internal class EventUnitOfWork : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EventUnitOfWork));
        private static readonly object SlowLock = new object();
        private static readonly HashSet<string> SlowEventTypes = new HashSet<string>();

        private static readonly Meter EventsMeter = Metric.Meter("Events", Unit.Commands);
        private static readonly Timer EventsTimer = Metric.Timer("Event Duration", Unit.Commands);

        private static readonly Meter ErrorsMeter = Metric.Meter("Event Errors", Unit.Errors);
        
        private readonly int _slowAlert;

        public EventUnitOfWork(int slowAlertThreshold)
        {
            _slowAlert = slowAlertThreshold;
        }

        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            if (!(context.Message.Instance is IEvent))
            {
                await next().ConfigureAwait(false);
                return;
            }

            var verbose = false;
            // Todo: break out timing of commands into a different pipeline step I think
            if (SlowEventTypes.Contains(context.Message.MessageType.FullName))
            {
                lock (SlowLock) SlowEventTypes.Remove(context.Message.MessageType.FullName);
                Logger.Write(LogLevel.Info, () => $"Event {context.Message.MessageType.FullName} was previously detected as slow, switching to more verbose logging (for this instance)\nPayload: {JsonConvert.SerializeObject(context.Message.Instance, Formatting.Indented).MaxLines(15)}");
                Defaults.MinimumLogging.Value = LogLevel.Info;
                verbose = true;
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
                        context.Extensions.TryGet(Defaults.Retries, out retries);
                        uow.Retries = retries;

                        await uow.Begin().ConfigureAwait(false);
                    }

                    s.Restart();

                    await next().ConfigureAwait(false);

                    s.Stop();
                    if (s.ElapsedMilliseconds > _slowAlert)
                    {
                        Logger.Write(LogLevel.Warn, () => $" - SLOW ALERT - Processing event {context.Message.MessageType.FullName} took {s.ElapsedMilliseconds} ms\nPayload: {JsonConvert.SerializeObject(context.Message.Instance, Formatting.Indented).MaxLines(15)}");
                        if (!verbose)
                            lock (SlowLock) SlowEventTypes.Add(context.Message.MessageType.FullName);
                    }
                    else
                        Logger.Write(LogLevel.Debug, () => $"Processing event {context.Message.MessageType.FullName} took {s.ElapsedMilliseconds} ms");

                    s.Restart();
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
                    s.Stop();
                    if (s.ElapsedMilliseconds > _slowAlert)
                        Logger.Write(LogLevel.Warn, () => $" - SLOW ALERT - UOW.End for event {context.Message.MessageType.FullName} took {s.ElapsedMilliseconds} ms");
                    else
                        Logger.Write(LogLevel.Debug, () => $"UOW.End for event {context.Message.MessageType.FullName} took {s.ElapsedMilliseconds} ms");

                }
            }
            catch (Exception e)
            {
                Logger.WriteFormat(LogLevel.Warn, "Caught exception '{0}' while executing event", e);
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
            finally
            {
                if (verbose)
                {
                    Logger.Write(LogLevel.Info, () => $"Finished processing event {context.Message.MessageType.FullName} verbosely - resetting log level");
                    Defaults.MinimumLogging.Value = null;
                }
            }
        }
    }
}

