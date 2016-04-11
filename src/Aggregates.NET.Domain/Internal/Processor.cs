using Aggregates.Contracts;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class Processor : IProcessor
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
