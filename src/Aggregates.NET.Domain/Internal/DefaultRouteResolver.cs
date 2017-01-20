using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Aggregates.Contracts;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;

namespace Aggregates.Internal
{
    class DefaultRouteResolver : IRouteResolver
    {
        private static readonly ILog Logger = LogManager.GetLogger("DefaultRouteResolver");

        // Yuck!
        private static readonly Dictionary<Type, IDictionary<string, Action<IEventSource, object>>> Cache = new Dictionary<Type, IDictionary<string, Action<IEventSource, object>>>();
        private static readonly object Lock = new object();
        private readonly IMessageMapper _mapper;

        public DefaultRouteResolver(IMessageMapper mapper)
        {
            _mapper = mapper;
        }

        private IDictionary<string, Action<IEventSource, object>> GetCached(IEventSource eventsource, Type eventType)
        {
            // Wtf is going on here? Well allow me to explain
            // In our eventsources we have methods that look like:
            // private void Handle(Events.MyEvent e) {}
            // and we may also have
            // private void Conflict(Events.MyEvent e) {}
            // this little cache GetOrAdd is basically searching for those methods and returning an Action the caller 
            // can use to execute the method 
            var mappedType = _mapper.GetMappedTypeFor(eventType);
            IDictionary<string, Action<IEventSource, object>> handles;
            lock (Lock)
            {
                if (Cache.TryGetValue(eventType, out handles))
                    return handles;
            }

            var methods = eventsource.GetType()
                                     .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                                     .Where(
                                            m => (m.Name == "Handle" || m.Name == "Conflict") &&
                                             m.GetParameters().Length == 1 &&
                                             m.GetParameters().Single().ParameterType == mappedType &&
                                             m.ReturnParameter.ParameterType == typeof(void));
            //.Select(m => new { Method = m, MessageType = m.GetParameters().Single().ParameterType });

            var methodInfos = methods as MethodInfo[] ?? methods.ToArray();
            if (!methodInfos.Any())
                return null;

            handles = methodInfos.ToDictionary(x => x.Name, x => (Action<IEventSource, object>)((es, m) =>
            {
                try
                {
                    x.Invoke(es, new[] { m });
                }
                catch (TargetInvocationException e)
                {
                    ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                }
            }));

            lock (Lock)
            {
                return Cache[eventType] = handles;
            }
        }

        public Action<IEventSource, object> Resolve(IEventSource eventsource, Type eventType)
        {
            var result = GetCached(eventsource, eventType);
            if (result == null) return null;

            Action<IEventSource, object> handle;
            if (!result.TryGetValue("Handle", out handle))
                handle = null;
            return handle;
        }
        public Action<IEventSource, object> Conflict(IEventSource eventsource, Type eventType)
        {
            var result = GetCached(eventsource, eventType);
            if (result == null) return null;

            Action<IEventSource, object> handle;
            if (!result.TryGetValue("Conflict", out handle))
                handle = null;
            return handle;
        }

    }
}
