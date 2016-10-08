using System.Threading.Tasks;

namespace Aggregates
{
    public interface IHandleComputed<in TCompute, TResponse> where TCompute : IComputed<TResponse>
    {
        Task<TResponse> Handle(TCompute compute);
    }
}
