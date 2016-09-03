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


    internal class CommandUnitOfWork : IBehavior<IncomingContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(CommandUnitOfWork));
        private static HashSet<String> SlowEventTypes = new HashSet<String>();

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
            try
            {
                // Todo: break out timing of commands into a different pipeline step I think
                if (SlowEventTypes.Contains(context.IncomingLogicalMessage.MessageType.FullName))
                {
                    Logger.WriteFormat(LogLevel.Info, "Command {0} was previously detected as slow, switching to more verbose logging (for this instance)", context.IncomingLogicalMessage.MessageType.FullName);
                    Defaults.MinimumLogging.Value = LogLevel.Info;
                }
            }
            catch (KeyNotFoundException) { }

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
                    
                    s.Restart();

                    next();

                    s.Stop();
                    if (s.ElapsedMilliseconds > _slowAlert)
                    {
                        Logger.WriteFormat(LogLevel.Warn, " - SLOW ALERT - Processing command {0} took {1} ms", context.IncomingLogicalMessage.MessageType.FullName, s.ElapsedMilliseconds);
                        if (!SlowEventTypes.Contains(context.IncomingLogicalMessage.MessageType.FullName))
                            SlowEventTypes.Add(context.IncomingLogicalMessage.MessageType.FullName);
                    }
                    else
                        Logger.WriteFormat(LogLevel.Debug, "Processing command {0} took {1} ms", context.IncomingLogicalMessage.MessageType.FullName, s.ElapsedMilliseconds);
                
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
                    if (s.ElapsedMilliseconds > _slowAlert)
                        Logger.WriteFormat(LogLevel.Warn, " - SLOW ALERT - UOW.End for command {0} took {1} ms", context.IncomingLogicalMessage.MessageType.FullName, s.ElapsedMilliseconds);
                    else
                        Logger.WriteFormat(LogLevel.Debug, "UOW.End for command {0} took {1} ms", context.IncomingLogicalMessage.MessageType.FullName, s.ElapsedMilliseconds);
                    
                }


            }
            catch (System.AggregateException e)
            {
                Logger.WriteFormat(LogLevel.Warn, "Caught exceptions '{0}' while executing command", e.InnerExceptions.Select(x => x.Message).Aggregate((c,n)=> $"{c}, {n}"));
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
            finally
            {
                try
                {
                    if (SlowEventTypes.Contains(context.IncomingLogicalMessage.MessageType.FullName) && Defaults.MinimumLogging.Value.HasValue)
                    {
                        Logger.WriteFormat(LogLevel.Info, "Finished processing command {0} verbosely - resetting log level", context.IncomingLogicalMessage.MessageType.FullName);
                        Defaults.MinimumLogging.Value = null;
                        SlowEventTypes.Remove(context.IncomingLogicalMessage.MessageType.FullName);
                    }
                }
                catch (KeyNotFoundException) { }
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

