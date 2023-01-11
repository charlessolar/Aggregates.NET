namespace Aggregates.Application
{
    public static class UnitOfWorkExtensions
    {
        public static TUnitOfWork Uow<TUnitOfWork>(this IServiceContext context) where TUnitOfWork : UnitOfWork.IApplicationUnitOfWork
        {
            return (TUnitOfWork)context.App;
        }
    }
}
