using Aggregates.Contracts;
using Aggregates.Extensions;
using Metrics;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.Pipeline;
using NServiceBus.Pipeline.Contexts;
using NServiceBus.Settings;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    internal class TesterBehavior : IBehavior<IncomingContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(TesterBehavior));

        private class StepObserver : IObserver<StepStarted>
        {
            private Guid ChainId = Guid.NewGuid();

            public void OnCompleted()
            {
                Logger.InfoFormat("Step completed in chain {0}", ChainId);
            }

            public void OnError(Exception error)
            {
                Logger.InfoFormat("Error processing step in chain {1}: {0}", error, ChainId);
            }

            public void OnNext(StepStarted value)
            {
                Logger.InfoFormat("Stepping into behavior {0} id {1} chain {2}", value.Behavior.Name, value.StepId, ChainId);
            }
        }
        public void Invoke(IncomingContext context, Action next)
        {
            var observer = context.Get<IObservable<StepStarted>>("Diagnostics.Pipe");
            observer.Subscribe(new StepObserver());

            next();
        }
    }
    internal class TesterBehaviorRegistration : RegisterStep
    {
        public TesterBehaviorRegistration()
            : base("TesterBehavior", typeof(TesterBehavior), "Begins and Ends command unit of work")
        {
            InsertBefore(WellKnownStep.ProcessingStatistics);

        }
    }


    internal class CommandUnitOfWork : IBehavior<IncomingContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(CommandUnitOfWork));

        private static Meter _commandsMeter = Metric.Meter("Commands", Unit.Commands);
        private static Metrics.Timer _commandsTimer = Metric.Timer("Command Duration", Unit.Commands);

        private static Meter _errorsMeter = Metric.Meter("Command Errors", Unit.Errors);

        private readonly ReadOnlySettings _settings;
        private readonly Int32 _slowAlert;
        public CommandUnitOfWork(IBus bus, ReadOnlySettings settings)
        {
            _settings = settings;
            _slowAlert = _settings.Get<Int32>("SlowAlertThreshold");
        }

        public void Invoke(IncomingContext context, Action next)
        {
            Stopwatch s = new Stopwatch();
            var uows = new ConcurrentStack<ICommandUnitOfWork>();
            try
            {
                _commandsMeter.Mark();
                using (_commandsTimer.NewContext())
                {
                    context.Builder.BuildAll<ICommandUnitOfWork>().ForEachAsync(2, async (uow) =>
                    {
                        uows.Push(uow);
                        uow.Builder = context.Builder;

                        var retries = 0;
                        context.TryGet<Int32>("AggregatesNet.Retries", out retries);
                        uow.Retries = retries;

                        await uow.Begin();
                    }).Wait();

                    if (Logger.IsDebugEnabled)
                        s.Restart();

                    next();

                    if (Logger.IsDebugEnabled)
                    {
                        s.Stop();
                        Logger.DebugFormat("Processing command {0} took {1} ms", context.IncomingLogicalMessage.MessageType.FullName, s.ElapsedMilliseconds);

                    }
                    s.Restart();
                    uows.Generate().ForEachAsync(2, async (uow) =>
                    {
                        try
                        {
                            await uow.End();
                        }
                        catch
                        {
                            // If it failed it needs to go back on the stack
                            uows.Push(uow);
                            throw;
                        }
                    }).Wait();
                    s.Stop();
                    if (Logger.IsDebugEnabled)
                    {
                        Logger.DebugFormat("UOW.End for command {0} took {1} ms", context.IncomingLogicalMessage.MessageType.FullName, s.ElapsedMilliseconds);
                    }
                    if (s.ElapsedMilliseconds > _slowAlert)
                    {
                        Logger.WarnFormat(" - SLOW ALERT - UOW.End for command {0} took {1} ms", context.IncomingLogicalMessage.MessageType.FullName, s.ElapsedMilliseconds);
                    }
                }


            }
            catch (System.AggregateException e)
            {
                _errorsMeter.Mark();
                var trailingExceptions = new List<Exception>();
                uows.Generate().ForEachAsync(2, async (uow) =>
                {
                    try
                    {
                        await uow.End(e);
                    }
                    catch (Exception endException)
                    {
                        trailingExceptions.Add(endException);
                    }
                }).Wait();


                if (trailingExceptions.Any())
                {
                    trailingExceptions.Insert(0, e);
                    e = new System.AggregateException(trailingExceptions);
                }
                throw;
            }
        }
    }

    internal class CommandUnitOfWorkRegistration : RegisterStep
    {
        public CommandUnitOfWorkRegistration()
            : base("CommandUnitOfWork", typeof(CommandUnitOfWork), "Begins and Ends command unit of work")
        {
            InsertBefore(WellKnownStep.ExecuteUnitOfWork);
            InsertAfter(WellKnownStep.CreateChildContainer);
        }
    }
}
