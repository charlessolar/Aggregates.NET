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
    public class QueryProcessor :IQueryProcessor
    {
        private readonly IBuilder _builder;

        public QueryProcessor(IBuilder builder)
        {
            _builder = builder;
        }

        [DebuggerStepThrough]
        public IEnumerable<TResponse> Process<TResponse, TQuery>(TQuery query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>
        {
            var handlerType = typeof(IHandleQueries<,>).MakeGenericType(query.GetType(), typeof(TResponse));

            dynamic handler = _builder.Build(handlerType);

            return handler.Handle(query);
        }
    }
}
