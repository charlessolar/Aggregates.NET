using Aggregates.Contracts;
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
        private readonly IBuilder _builder;

        public Processor(IBuilder builder)
        {
            _builder = builder;
        }

        [DebuggerStepThrough]
        public IEnumerable<TResponse> Process<TResponse, TQuery>(TQuery query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>
        {
            var handlerType = typeof(IHandleQueries<,>).MakeGenericType(query.GetType(), typeof(TResponse));

            dynamic handler = _builder.Build(handlerType);

            return handler.Handle((dynamic)query);
        }
        [DebuggerStepThrough]
        public TResponse Compute<TResponse, TComputed>(TComputed compute) where TComputed : IComputed<TResponse>
        {
            var handlerType = typeof(IHandleComputed<,>).MakeGenericType(compute.GetType(), typeof(TResponse));

            dynamic handler = _builder.Build(handlerType);

            return handler.Handle((dynamic)compute);
        }
    }
}
