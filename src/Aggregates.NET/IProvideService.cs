using System.Threading.Tasks;

namespace Aggregates
{
    public interface IProvideService<in TService, TResponse> where TService : IService<TResponse>
    {
        Task<TResponse> Handle(TService query, IServiceContext context);
    }
}
