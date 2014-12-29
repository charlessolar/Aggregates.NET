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

        private readonly IRouteResolver _resolver;
        private readonly IDictionary<Type, Action<Object>> _handlers;

        public EventRouter(IRouteResolver resolver)
        {
            _resolver = resolver;
            _handlers = new Dictionary<Type, Action<Object>>();
        }

        public void Register<TId>(Aggregate<TId> aggregate)
        {
            foreach (var route in _resolver.Resolve(aggregate))
                this._handlers[route.Key] = route.Value;
        }

        public void Register(Type eventType, Action<Object> handler)
        {
            this._handlers[@eventType] = handler;
        }

        public Action<Object> RouteFor(Type eventType)
        {
            Action<Object> handler;
            if (!_handlers.TryGetValue(eventType, out handler))
            {
                throw new HandlerNotFoundException(String.Format("No handler for event {0}", eventType.Name));
            }

            return e => handler(e);
        }
    }
}