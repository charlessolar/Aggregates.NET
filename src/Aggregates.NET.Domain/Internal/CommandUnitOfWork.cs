using Metrics;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Pipeline;
using NServiceBus.Pipeline.Contexts;
using System;
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

        public void Invoke(IncomingContext context, Action next)
        {
            Stopwatch s = null;
            var uows = new Stack<ICommandUnitOfWork>();
            try
            {
                _commandsMeter.Mark();
                using (_commandsTimer.NewContext())
                {
                    if (Logger.IsDebugEnabled)
                        s = Stopwatch.StartNew();
                    foreach (var uow in context.Builder.BuildAll<ICommandUnitOfWork>())
                    {
                        uows.Push(uow);
                        uow.Builder = context.Builder;
                        uow.Begin();
                    }

                    if (Logger.IsDebugEnabled)
                    {
                        s.Stop();
                        //Logger.DebugFormat("UOW.Begin for command {0} took {1} ms", context.IncomingLogicalMessage.MessageType.FullName, s.ElapsedMilliseconds);
                    }
                    if (Logger.IsDebugEnabled)
                        s.Restart();
                    next();

                    if (Logger.IsDebugEnabled)
                    {
                        s.Stop();
                        //Logger.DebugFormat("Processing command {0} took {1} ms", context.IncomingLogicalMessage.MessageType.FullName, s.ElapsedMilliseconds);
                        s.Restart();
                    }

                    while (uows.Count > 0)
                    {
                        uows.Pop().End();
                    }
                    if (Logger.IsDebugEnabled)
                    {
                        s.Stop();
                        //Logger.DebugFormat("UOW.End for command {0} took {1} ms", context.IncomingLogicalMessage.MessageType.FullName, s.ElapsedMilliseconds);
                    }
                }
            }
            catch (Exception e)
            {
                _errorsMeter.Mark();
                var trailingExceptions = new List<Exception>();
                while (uows.Count > 0)
                {
                    var uow = uows.Pop();
                    try
                    {
                        uow.End(e);
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

    internal class CommandUnitOfWorkRegistration : RegisterStep
    {
        public CommandUnitOfWorkRegistration()
            : base("CommandUnitOfWork", typeof(CommandUnitOfWork), "Begins and Ends command unit of work")
        {
            InsertBefore(WellKnownStep.LoadHandlers);
        }
    }
}
