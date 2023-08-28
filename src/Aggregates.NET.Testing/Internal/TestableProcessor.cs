using Newtonsoft.Json;
using NServiceBus.MessageInterfaces.MessageMapper.Reflection;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class TestableProcessor : ITestableProcessor
    {
        private readonly TestableEventFactory _factory;

        public TestableProcessor()
        {
            _factory = new TestableEventFactory(new MessageMapper());
            Planned = new Dictionary<string, object>();
            Requested = new List<string>();
        }

        internal Dictionary<string, object> Planned;
        internal List<string> Requested;

        public IServicePlanner<TService, TResponse> Plan<TService, TResponse>(TService service) where TService : IService<TResponse>
        {
            return new ServicePlanner<TService, TResponse>(this, service);
        }
        public IServiceChecker<TService, TResponse> Check<TService, TResponse>(TService service) where TService : IService<TResponse>
        {
            return new ServiceChecker<TService, TResponse>(this, service);
        }
        public Task<TResponse> Process<TService, TResponse>(TService service, IServiceProvider container) where TService : IService<TResponse>
        {
            var serviceString = JsonConvert.SerializeObject(service);
            var key = $"{typeof(TService).FullName}.{serviceString}";
            if (!Planned.ContainsKey(key))
                throw new ArgumentException($"Service {typeof(TService).FullName} body {serviceString} was not planned");
            Requested.Add(key);
            return Task.FromResult((TResponse)Planned[key]);
        }

        public Task<TResponse> Process<TService, TResponse>(Action<TService> service, IServiceProvider container) where TService : IService<TResponse>
        {
            return Process<TService, TResponse>(_factory.Create(service), container);
        }
    }
}
