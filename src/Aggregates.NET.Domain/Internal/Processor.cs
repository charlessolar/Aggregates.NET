using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Aggregates.Contracts;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Internal
{
    class Processor : IProcessor
    {
        [DebuggerStepThrough]
        public Task<IEnumerable<TResponse>> Process<TQuery, TResponse>(IBuilder builder, TQuery query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>
        {
            var handlerType = typeof(IHandleQueries<,>).MakeGenericType(typeof(TQuery), typeof(TResponse));

            dynamic handler = builder.Build(handlerType);

            return handler.Handle((dynamic)query);
        }
        [DebuggerStepThrough]
        public Task<TResponse> Compute<TComputed, TResponse>(IBuilder builder, TComputed compute) where TComputed : IComputed<TResponse>
        {
            var handlerType = typeof(IHandleComputed<,>).MakeGenericType(typeof(TComputed), typeof(TResponse));

            dynamic handler = builder.Build(handlerType);

            return handler.Handle((dynamic)compute);
        }
    }
}
