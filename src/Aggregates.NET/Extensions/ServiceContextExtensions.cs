using Aggregates.Contracts;

namespace Aggregates
{
    public static class ServiceContextExtensions
    {
        public static TUnitOfWork Application<TUnitOfWork>(this IServiceContext context) where TUnitOfWork : UnitOfWork.IApplicationUnitOfWork
        {
            return (TUnitOfWork)context.App;
        }
        public static UnitOfWork.IDomainUnitOfWork Uow(this IServiceContext context) 
        {
            return context.Domain;
        }
        public static IRepository<T> For<T>(this IServiceContext context) where T : class, IEntity
        {
            return context.Domain.For<T>();
        }
    }
}
