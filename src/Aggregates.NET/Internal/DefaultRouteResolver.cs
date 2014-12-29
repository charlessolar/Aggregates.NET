using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.Logging;
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
        private readonly IMessageCreator _eventFactory;
        public DefaultRouteResolver(IMessageCreator eventFactory)
        {
            _eventFactory = eventFactory;
        }
        private static readonly ILog Logger = LogManager.GetLogger(typeof(DefaultRouteResolver));
        public IDictionary<Type, Action<Object>> Resolve<TId>(Aggregate<TId> aggregate)
        {
            var name = aggregate.GetType().Name;

            var handleMethods = aggregate.GetType()
                                 .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                                 .Where(
                                     m => m.Name == "Handle" && m.GetParameters().Length == 1 && m.ReturnParameter.ParameterType == typeof(void))
                                 .Select(m => new { Method = m, MessageType = m.GetParameters().Single().ParameterType });

            var ret = new Dictionary<Type, Action<Object>>();
            foreach (var method in handleMethods)
            {
                Logger.DebugFormat("Handle method found on aggregate Type '{0}' for event Type '{1}'", name, method.MessageType);

                // Also add the factory type since that is what is really being used
                var factoryType = _eventFactory.CreateInstance(method.MessageType);

                ret.Add(method.MessageType, m => method.Method.Invoke(aggregate, new[] { m }));
                ret.Add(factoryType.GetType(), m => method.Method.Invoke(aggregate, new[] { m }));
            }
            return ret;
        }
    }
}
