using NServiceBus;

namespace Aggregates.Application
{
    public static class UnitOfWorkExtensions
    {
        public static UnitOfWork.IApplicationUnitOfWork Uow(this IMessageHandlerContext context)
        {
            return context.Uow<UnitOfWork.IApplicationUnitOfWork>();
        }
    }
}
