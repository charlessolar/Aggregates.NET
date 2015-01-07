using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class DefaultRouteResolver : IRouteResolver
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(DefaultRouteResolver));

        private IDictionary<Type, Action<Object>> _cache;
        private IMessageMapper _mapper;

        public DefaultRouteResolver(IMessageMapper mapper)
        {
            _mapper = mapper;
            _cache = new Dictionary<Type, Action<Object>>();
        }

        public Action<Object> Resolve<TId>(IEventSource<TId> eventsource, Type eventType)
        {
            var mappedType = _mapper.GetMappedTypeFor(eventType);

            Action<Object> cached = null;
            if (_cache.TryGetValue(mappedType, out cached))
                return cached;
            
            var handleMethod = eventsource.GetType()
                                 .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                                 .SingleOrDefault(
                                        m => m.Name == "Handle" &&
                                         m.GetParameters().Length == 1 &&
                                         m.GetParameters().Single().ParameterType == mappedType &&
                                         m.ReturnParameter.ParameterType == typeof(void));
                                 //.Select(m => new { Method = m, MessageType = m.GetParameters().Single().ParameterType });


            if (handleMethod == null)
            {
                Logger.WarnFormat("No handle method found on type '{0}' for event Type '{1}'", eventsource.GetType().Name, mappedType.FullName);
                _cache.Add(mappedType, null);
                return null;
            }

            Action<Object> action = m => handleMethod.Invoke(eventsource, new[] { m });
            _cache.Add(mappedType, action);

            Logger.DebugFormat("Handle method found on type '{0}' for event Type '{1}'", eventsource.GetType().Name, mappedType.FullName);
            return action;
        }
    }
}
