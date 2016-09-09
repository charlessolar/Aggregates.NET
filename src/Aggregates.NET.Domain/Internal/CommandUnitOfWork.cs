using Aggregates.Contracts;
using Aggregates.Extensions;
using Metrics;
using Newtonsoft.Json;
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
                    Logger.Write(LogLevel.Info, () => $"Command {context.IncomingLogicalMessage.MessageType.FullName} was previously detected as slow, switching to more verbose logging (for this instance)\nPayload: {JsonConvert.SerializeObject(context.IncomingLogicalMessage.Instance, Formatting.Indented).MaxLines(15)}");
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
                    foreach( var uow in context.Builder.BuildAll<ICommandUnitOfWork>()) 
                    {
                        uows.Push(uow);
                        uow.Builder = context.Builder;

                        var retries = 0;
                        context.TryGet<Int32>("AggregatesNet.Retries", out retries);
                        uow.Retries = retries;

                        uow.Begin().Wait();
                    }
                    
                    s.Restart();

                    next();

                    s.Stop();
                    if (s.ElapsedMilliseconds > _slowAlert)
                    {
                        Logger.Write(LogLevel.Warn, () => $" - SLOW ALERT - Processing command {context.IncomingLogicalMessage.MessageType.FullName} took {s.ElapsedMilliseconds} ms\nPayload: {JsonConvert.SerializeObject(context.IncomingLogicalMessage.Instance, Formatting.Indented).MaxLines(15)}");
                        if (!SlowEventTypes.Contains(context.IncomingLogicalMessage.MessageType.FullName))
                            SlowEventTypes.Add(context.IncomingLogicalMessage.MessageType.FullName);
                    }
                    else
                        Logger.Write(LogLevel.Debug, () => $"Processing command {context.IncomingLogicalMessage.MessageType.FullName} took {s.ElapsedMilliseconds} ms");
                
                    s.Restart();
                    foreach (var uow in uows.Generate())
                    {
                        try
                        {
                            uow.End().Wait();
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
                        Logger.Write(LogLevel.Warn, () => $" - SLOW ALERT - UOW.End for command {context.IncomingLogicalMessage.MessageType.FullName} took {s.ElapsedMilliseconds} ms");
                    else
                        Logger.Write(LogLevel.Debug, () => $"UOW.End for command {context.IncomingLogicalMessage.MessageType.FullName} took {s.ElapsedMilliseconds} ms");
                    
                }


            }
            catch (System.AggregateException e)
            {
                Logger.WriteFormat(LogLevel.Warn, "Caught exceptions '{0}' while executing command", e.InnerExceptions.Select(x => x.Message).Aggregate((c,n)=> $"{c}, {n}"));
                _errorsMeter.Mark();
                var trailingExceptions = new List<Exception>();
                foreach (var uow in uows.Generate())
                {
                    try
                    {
                        uow.End(e).Wait();
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
                try
                {
                    if (SlowEventTypes.Contains(context.IncomingLogicalMessage.MessageType.FullName) && Defaults.MinimumLogging.Value.HasValue)
                    {
                        Logger.Write(LogLevel.Info, () => $"Finished processing command {context.IncomingLogicalMessage.MessageType.FullName} verbosely - resetting log level");
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

