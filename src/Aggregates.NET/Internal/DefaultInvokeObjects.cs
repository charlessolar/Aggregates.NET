using Aggregates.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class DefaultInvokeObjects : IInvokeObjects
    {
        private ConcurrentDictionary<String, Func<Object, Object, IHandleContext, Task>> _cache;

        public DefaultInvokeObjects()
        {
            _cache = new ConcurrentDictionary<String, Func<Object, Object, IHandleContext, Task>>();
        }
        public Func<Object, Object, IHandleContext, Task> Invoker(Object handler, Type messageType)
        {
            var handlerType = handler.GetType();
            var key = $"{handlerType.FullName}+{messageType.Name}";
            return _cache.GetOrAdd(key, (k) =>
            {

                var handleMethod = handlerType
                                     .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                     .SingleOrDefault(
                                            m => m.Name == "Handle" &&
                                             m.GetParameters().Length == 2 &&
                                             m.GetParameters().First().ParameterType == messageType &&
                                             m.ReturnParameter.ParameterType == typeof(Task));
                //.Select(m => new { Method = m, MessageType = m.GetParameters().Single().ParameterType });

                if (handleMethod == null)
                    return null;

                Func<Object, Object, IHandleContext, Task> action = (h, m, context) => (Task)handleMethod.Invoke(h, new[] { m, context });
                return action;
            });
        }
    }
}
