using Aggregates.Contracts;
using Aggregates.Internal;
using Aggregates.Sagas;
using Microsoft.Extensions.DependencyInjection;
using NServiceBus;
using NServiceBus.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Domain
{
    public static class UnitOfWorkExtensions
    {
        public static UnitOfWork.IDomainUnitOfWork Uow(this IServiceContext context)
        {
            return context.Uow<UnitOfWork.IDomainUnitOfWork>();
        }
        public static IRepository<T> For<T>(this IMessageHandlerContext context) where T : class, IEntity
        {
            var uow = context.Extensions.Get<UnitOfWork.IDomainUnitOfWork>();
            return uow.For<T>();
        }

        public static Task SendToSelf(this IMessageHandlerContext context, Messages.ICommand command)
        {
            var provider = context.Extensions.Get<IServiceProvider>();
            var dispatcher = provider.GetRequiredService<IMessageDispatcher>();

            var message = new FullMessage
            {
                Headers = context.MessageHeaders.Where(x => x.Key != $"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}").ToDictionary(kv => kv.Key, kv => kv.Value),
                Message = command
            };
            _ = Task.Run(() => dispatcher.SendLocal(message), context.CancellationToken);
            return Task.CompletedTask;
        }

        public static CommandSaga Saga(this IMessageHandlerContext context, Id sagaId, string domainDestination = null)
        {
            if (domainDestination == null)
                domainDestination = context.GetSettings()?.CommandDestination;

            if (string.IsNullOrEmpty(domainDestination) && !context.Extensions.TryGet("CommandDestination", out domainDestination))
                throw new ArgumentException("Configuration lacks CommandDestination [Configuration.SetCommandDestination]");
            // Don't know if this is the best way to get the current message
            var currentMessage = (context as IInvokeHandlerContext)?.MessageBeingHandled as Messages.IMessage;

            return new CommandSaga(context, sagaId.ToString(), currentMessage, domainDestination);
        }
    }
}
