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
using System.Diagnostics.CodeAnalysis;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class Processor : IProcessor
    {
        private static readonly ILog Logger = LogProvider.GetLogger("Processor");
        private static readonly ConcurrentDictionary<Type, object> Processors = new ConcurrentDictionary<Type, object>();

        [DebuggerStepThrough]
        public Task<TResponse> Process<TService, TResponse>(TService service, IContainer container) where TService : IService<TResponse>
        {
            var handlerType = typeof(IProvideService<,>).MakeGenericType(typeof(TService), typeof(TResponse));

            var handlerFunc = (Func<object, TService, IServiceContext, Task<TResponse>>)Processors.GetOrAdd(handlerType, t => ReflectionExtensions.MakeServiceHandler<TService, TResponse>(handlerType));
            var handler = container.Resolve(handlerType);
            if (handler == null)
            {
                Logger.ErrorEvent("ProcessFailure", "No handler [{ServiceType:l}] response [{Response:l}]", typeof(TService).FullName, typeof(TResponse).FullName);
                return Task.FromResult(default(TResponse));
            }

            // Todo: both units of work should come from the pipeline not the container
            var context = new HandleContext(container.Resolve<IDomainUnitOfWork>(), container.Resolve<IAppUnitOfWork>(), container);

            return handlerFunc(handler, service, context);
        }
    }
}
