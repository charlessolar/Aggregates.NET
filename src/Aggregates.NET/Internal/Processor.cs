using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Messages;
using System.Collections.Concurrent;
using Aggregates.Extensions;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class Processor : IProcessor
    {
        private static readonly ConcurrentDictionary<Type, object> Processors = new ConcurrentDictionary<Type, object>();

        [DebuggerStepThrough]
        public Task<TResponse> Process<TService, TResponse>(TService service, IContainer container) where TService : IService<TResponse>
        {
            var factory = container.Resolve<ILoggerFactory>();
            var handlerType = typeof(IProvideService<,>).MakeGenericType(typeof(TService), typeof(TResponse));

            var handlerFunc = (Func<object, TService, IServiceContext, Task<TResponse>>)Processors.GetOrAdd(handlerType, t => ReflectionExtensions.MakeServiceHandler<TService, TResponse>(handlerType));
            var handler = container.Resolve(handlerType);
            if (handler == null)
            {
                var logger = factory.CreateLogger("Processor");
                logger.ErrorEvent("ProcessFailure", "No handler [{ServiceType:l}] response [{Response:l}]", typeof(TService).FullName, typeof(TResponse).FullName);
                return Task.FromResult(default(TResponse));
            }

            // Todo: both units of work should come from the pipeline not the container
            var context = new HandleContext(container.Resolve<Aggregates.UnitOfWork.IDomain>(), container.Resolve<Aggregates.UnitOfWork.IApplication>(), container.Resolve<IProcessor>(), container);

            return handlerFunc(handler, service, context);
        }

        public Task<TResponse> Process<TService, TResponse>(Action<TService> service, IContainer container) where TService : IService<TResponse>
        {
            var factory = container.Resolve<IEventFactory>();
            return Process<TService, TResponse>(factory.Create(service), container);
        }
    }
}
