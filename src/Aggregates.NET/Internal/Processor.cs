using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Messages;
using System.Collections.Concurrent;
using Aggregates.Extensions;
using Aggregates.Logging;

namespace Aggregates.Internal
{
    class Processor : IProcessor
    {
        private static readonly ILog Logger = LogProvider.GetLogger("Processor");
        private static readonly ConcurrentDictionary<Type, object> Processors = new ConcurrentDictionary<Type, object>();

        [DebuggerStepThrough]
        public Task<TResponse> Process<TQuery, TResponse>(TQuery query, IContainer container) where TQuery : IQuery<TResponse>
        {
            var handlerType = typeof(IHandleQueries<,>).MakeGenericType(typeof(TQuery), typeof(TResponse));

            var handlerFunc = (Func<object, TQuery, IDomainUnitOfWork, Task<TResponse>>)Processors.GetOrAdd(handlerType, t => ReflectionExtensions.MakeQueryHandler<TQuery, TResponse>(handlerType));
            var handler = container.Resolve(handlerType);
            if (handler == null)
            {
                Logger.Error($"No query handler for query type: {typeof(TQuery).FullName} response type: {typeof(TResponse).FullName}");
                return null;
            }

            return handlerFunc(handler, query, container.Resolve<IDomainUnitOfWork>());
        }
    }
}
