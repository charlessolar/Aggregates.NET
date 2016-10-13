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


    internal class CommandUnitOfWork : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(CommandUnitOfWork));

        private static readonly Meter CommandsMeter = Metric.Meter("Commands", Unit.Commands);
        private static readonly Timer CommandsTimer = Metric.Timer("Command Duration", Unit.Commands);

        private static readonly Meter ErrorsMeter = Metric.Meter("Command Errors", Unit.Errors);
        


        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            if(!(context.Message.Instance is ICommand))
            {
                await next().ConfigureAwait(false);
                return;
            }


            var uows = new ConcurrentStack<ICommandUnitOfWork>();
            try
            {
                CommandsMeter.Mark();
                using (CommandsTimer.NewContext())
                {
                    foreach (var uow in context.Builder.BuildAll<ICommandUnitOfWork>())
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
                Logger.WriteFormat(LogLevel.Warn, "Caught exception '{0}' while executing command {1}", e.GetType().FullName, context.Message.MessageType.FullName);
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

