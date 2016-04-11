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
        private ConcurrentDictionary<String, Func<Object, Object, Task>> _cache;

        public DefaultInvokeObjects()
        {
            _cache = new ConcurrentDictionary<String, Func<Object, Object, Task>>();
        }
        public Func<Object, Object, Task> Invoker(Object handler, Type messageType)
        {
            var handlerType = handler.GetType();
            var key = $"{handlerType.FullName}+{messageType.Name}";
            return _cache.GetOrAdd(key, (k) =>
            {

                var handleMethod = handlerType
                                     .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                     .SingleOrDefault(
                                            m => m.Name == "Handle" &&
                                             m.GetParameters().Length == 1 &&
                                             m.GetParameters().Single().ParameterType == messageType &&
                                             m.ReturnParameter.ParameterType == typeof(Task));
                //.Select(m => new { Method = m, MessageType = m.GetParameters().Single().ParameterType });

                if (handleMethod == null)
                    return null;

                Func<Object, Object, Task> action = (h, m) => (Task)handleMethod.Invoke(h, new[] { m });
                return action;
            });
        }
    }
}
