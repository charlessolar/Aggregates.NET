using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    public class UnitOfWorkExecutor : Behavior<IIncomingLogicalMessageContext>
    {
        private readonly ILogger Logger;

        private readonly ISettings _settings;
        private readonly IServiceProvider _provider;
        private readonly IMetrics _metrics;

        public UnitOfWorkExecutor(ILogger<UnitOfWorkExecutor> logger, ISettings settings, IServiceProvider provider, IMetrics metrics)
        {
            Logger = logger;
            _settings = settings;
            _provider = provider;
            _metrics = metrics;
        }

        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            var provider = context.Extensions.Get<IServiceProvider>();

            // Only SEND messages deserve a UnitOfWork
            if (context.GetMessageIntent() != MessageIntentEnum.Send && context.GetMessageIntent() != MessageIntentEnum.Publish)
            {
                await next().ConfigureAwait(false);
                return;
            }
            if (context.Message.MessageType.IsAssignableTo(typeof(Messages.Accept)) || context.Message.MessageType.IsAssignableTo(typeof(Messages.Reject)))
            {
                // If this happens the callback for the message took too long (likely due to a timeout)
                // normall NSB will report an exception for "No Handlers" - this will just log a warning and ignore
                Logger.WarnEvent("Overdue", "Overdue Accept/Reject {MessageType} callback - your timeouts might be too short", context.Message.MessageType.FullName);
                return;
            }

            Aggregates.UnitOfWork.IUnitOfWork uow = null;
            if (context.Message.Instance is Messages.ICommand)
            {
                uow = provider.GetService<Aggregates.UnitOfWork.IDomainUnitOfWork>();
                context.Extensions.Set(uow as Aggregates.UnitOfWork.IDomainUnitOfWork);
            }
            else
            {
                uow = provider.GetService<Aggregates.UnitOfWork.IApplicationUnitOfWork>();
                context.Extensions.Set(uow as Aggregates.UnitOfWork.IApplicationUnitOfWork);
            }

            // uow can be null if the message is an event and application unit of work was not defined.
            // this means the event can still read things but no changes will be committed anywhere

            // Set into the context because DI can be slow
            context.Extensions.Set(uow);

            var commitableUow = uow as Aggregates.UnitOfWork.IBaseUnitOfWork;
            try
            {
                _metrics.Increment("Messages Concurrent", Unit.Message);
                using (_metrics.Begin("Message Duration"))
                {
                    await (commitableUow?.Begin() ?? Task.CompletedTask).ConfigureAwait(false);

                    await next().ConfigureAwait(false);

                    await (commitableUow?.End() ?? Task.CompletedTask).ConfigureAwait(false);

                }

            }
            catch (Exception e)
            {
                if (!(e is BusinessException))
                {
                    // Logging and metrics for business exceptions happens upstream
                    Logger.WarnEvent("UOWException", e, "Received exception while processing message {MessageType}", context.Message.MessageType.FullName);
                    _metrics.Mark("Message Errors", Unit.Errors);
                }
                var trailingExceptions = new List<Exception>();

                try
                {
                    await (commitableUow?.End(e) ?? Task.CompletedTask).ConfigureAwait(false);
                }
                catch (Exception endException)
                {
                    trailingExceptions.Add(endException);
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
                _metrics.Decrement("Messages Concurrent", Unit.Message);
            }
        }

    }
    [ExcludeFromCodeCoverage]
    internal class UowRegistration : RegisterStep
    {
        public UowRegistration() : base(
            stepId: "UnitOfWorkExecution",
            behavior: typeof(UnitOfWorkExecutor),
            description: "Begins and Ends unit of work for your endpoint",
            factoryMethod: (b) => new UnitOfWorkExecutor(b.Build<ILogger<UnitOfWorkExecutor>>(), b.Build<ISettings>(), b.Build<IServiceProvider>(), b.Build<IMetrics>())
        )
        {
            InsertAfter("FailureReply");
        }
    }
}

