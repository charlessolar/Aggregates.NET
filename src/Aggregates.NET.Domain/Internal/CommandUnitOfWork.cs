using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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


    internal class CommandUnitOfWork : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(CommandUnitOfWork));

        private static readonly Meter CommandsMeter = Metric.Meter("Commands", Unit.Commands);
        private static readonly Timer CommandsTimer = Metric.Timer("Command Duration", Unit.Commands);

        private static readonly Meter ErrorsMeter = Metric.Meter("Command Errors", Unit.Errors);


        private readonly IPersistence _persistence;

        public CommandUnitOfWork(IPersistence persistence)
        {
            _persistence = persistence;
        }

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

                        int retries;
                        if (!context.Extensions.TryGet(Defaults.Attempts, out retries))
                            retries = 0;
                        uow.Retries = retries;

                        var savedBag =
                            await _persistence.Remove($"{context.MessageId}-{uow.GetType().FullName}")
                                    .ConfigureAwait(false);

                        uow.Bag = savedBag ?? new ContextBag();

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
                    await _persistence.Save($"{context.MessageId}-{uow.GetType().FullName}", uow.Bag).ConfigureAwait(false);
                }


                if (trailingExceptions.Any())
                {
                    trailingExceptions.Insert(0, e);
                    e = new System.AggregateException(trailingExceptions);
                }
                throw e;
            }
        }
    }
    internal class CommandUowRegistration : RegisterStep
    {
        public CommandUowRegistration() : base(
            stepId: "CommandUnitOfWork",
            behavior: typeof(CommandUnitOfWork),
            description: "Begins and Ends command unit of work"
        )
        {
        }
    }
}

