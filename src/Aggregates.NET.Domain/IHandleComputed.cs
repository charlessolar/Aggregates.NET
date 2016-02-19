using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public interface IHandleComputed<TCompute, TResponse> where TResponse : struct where TCompute : IComputed<TResponse>
    {
        TResponse Handle(TCompute query);
    }
}
