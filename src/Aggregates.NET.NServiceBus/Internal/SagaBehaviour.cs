using Aggregates.Contracts;
using Aggregates.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NServiceBus.Pipeline;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class SagaBehaviour : Behavior<IIncomingLogicalMessageContext>
    {
        private readonly ILogger Logger;

        private readonly IMetrics _metrics;
        private readonly IMessageDispatcher _dispatcher;

        public SagaBehaviour(ILogger<SagaBehaviour> logger, IMetrics metrics, IMessageDispatcher dispatcher)
        {
            Logger = logger;
            _metrics = metrics;
            _dispatcher = dispatcher;
        }

        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            // check header if was a saga message
            if (!context.MessageHeaders.TryGetValue(Defaults.SagaHeader, out var sagaId))
            {
                await next().ConfigureAwait(false);
                return;
            }

            if (context.Message.MessageType.IsAssignableTo(typeof(Messages.Accept)))
            {
                // substitute Accept with "ContinueSaga"
                context.UpdateMessageInstance(new Sagas.ContinueCommandSaga
                {
                    SagaId = sagaId
                });
            }
            else if (context.Message.MessageType.IsAssignableTo(typeof(Messages.Reject)))
            {
                // substitute Reject with "AbortSaga"
                context.UpdateMessageInstance(new Sagas.AbortCommandSaga
                {
                    SagaId = sagaId
                });
            }

            await next().ConfigureAwait(false);
        }
    }
    [ExcludeFromCodeCoverage]
    internal class SagaBehaviourRegistration : RegisterStep
    {
        public SagaBehaviourRegistration() : base(
            stepId: "SagaBehaviour",
            behavior: typeof(SagaBehaviour),
            description: "Handles internal sagas for consecutive command support",
            factoryMethod: (b) => new SagaBehaviour(b.GetService<ILogger<SagaBehaviour>>(), b.GetService<IMetrics>(), b.GetService<IMessageDispatcher>())
        )
        {
            InsertBefore("FailureReply");
        }
    }

}
