using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Aggregates.Contracts;
using NServiceBus.Logging;
using System.Collections.Generic;

namespace Aggregates.Internal
{
    public class EventRouter : IEventRouter
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EventRouter));
        private readonly IDictionary<Type, Action<Object>> _handlers = new Dictionary<Type, Action<Object>>();
        private String _aggregateName;

        public void Register<TId>(Aggregate<TId> aggregate)
        {
            _aggregateName = aggregate.GetType().Name;

            var handleMethods =
                        aggregate.GetType()
                                 .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                                 .Where(
                                     m => m.Name == "Handle" && m.GetParameters().Length == 1 && m.ReturnParameter.ParameterType == typeof(void))
                                 .Select(m => new { Method = m, MessageType = m.GetParameters().Single().ParameterType });

            foreach (var method in handleMethods)
            {
                Logger.DebugFormat("Handle method found on aggregate Type '{0}' for event Type '{1}'", _aggregateName, method.Method.Name);
                this._handlers.Add(method.MessageType, m => method.Method.Invoke(aggregate, new[] { m }));
            }
        }

        public void Register(Type eventType, Action<Object> handler)
        {
            this._handlers[@eventType] = handler;
        }

        public Action<Object> Get(Type eventType)
        {
            Action<Object> handler;
            if (!_handlers.TryGetValue(eventType, out handler))
            {
                throw new HandlerNotFoundException(String.Format("No handler on aggregate {0} for event {1}", _aggregateName, eventType.Name));
            }

            return e => handler(e);
        }
    }
}