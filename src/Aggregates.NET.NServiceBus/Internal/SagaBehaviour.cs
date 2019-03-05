using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Logging;
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
        private static readonly ILog Logger = LogProvider.GetLogger("SagaBehaviour");

        private readonly IMetrics _metrics;
        private readonly IMessageDispatcher _dispatcher;

        public SagaBehaviour(IMetrics metrics, IMessageDispatcher dispatcher)
        {
            _metrics = metrics;
            _dispatcher = dispatcher;
        }

        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            // check header if was a saga message
            if (!context.Headers.TryGetValue(Defaults.SagaHeader, out var sagaId))
            {
                await next().ConfigureAwait(false);
                return;
            }

            try
            {
                if (context.Message.MessageType == typeof(Messages.Accept))
                {
                    // substitute Accept with "ContinueSaga"
                    context.UpdateMessageInstance(new Sagas.ContinueCommandSaga
                    {
                        SagaId = sagaId
                    });
                    await next().ConfigureAwait(false);
                }
                else
                {
                    // substitute Reject with "AbortSaga"
                    context.UpdateMessageInstance(new Sagas.AbortCommandSaga
                    {
                        SagaId = sagaId
                    });
                }
            }
            // catch exceptions, send message to error queue
            catch (SagaWasAborted ex)
            {
                await _dispatcher.SendToError(ex, new FullMessage
                {
                    Message = ex.Originating,
                    Headers = context.Headers
                });
            }
            catch (SagaAbortionFailureException ex)
            {
                await _dispatcher.SendToError(ex, new FullMessage
                {
                    Message = ex.Originating,
                    Headers = context.Headers
                });
            }



        }
    }
    [ExcludeFromCodeCoverage]
    internal class SagaBehaviourRegistration : RegisterStep
    {
        public SagaBehaviourRegistration(IContainer container) : base(
            stepId: "SagaBehaviour",
            behavior: typeof(SagaBehaviour),
            description: "Handles internal sagas for consecutive command support",
            factoryMethod: (b) => new SagaBehaviour(container.Resolve<IMetrics>(), container.Resolve<IMessageDispatcher>())
        )
        {
            InsertBefore("ExceptionRejector");
        }
    }

}
