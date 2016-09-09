using Aggregates.Contracts;
using NServiceBus.ObjectBuilder;
using NServiceBus.Pipeline.Contexts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class OutgoingContextWrapper : IOutgoingContextAccessor
    {
        private readonly OutgoingContext _context;

        public OutgoingContextWrapper(OutgoingContext context)
        {
            _context = context;
        }

        public IBuilder Builder
        {
            get
            {
                return _context.Builder;
            }
        }

        public object OutgoingLogicalMessageInstance
        {
            get
            {
                return _context.OutgoingLogicalMessage.Instance;
            }
        }
        public Type OutgoingLogicalMessageMessageType
        {
            get
            {
                return _context.OutgoingLogicalMessage.MessageType;
            }
        }
        public void UpdateMessageInstance(object msg)
        {
            _context.OutgoingLogicalMessage.UpdateMessageInstance(msg);
        }

        public void Set<T>(string name, T value)
        {
            _context.Set<T>(name, value);
        }
        public void Set<T>(T value)
        {
            _context.Set(value);
        }
        public T Get<T>(String name)
        {
            return _context.Get<T>(name);
        }
        public T Get<T>()
        {
            return _context.Get<T>();
        }
        public Boolean TryGet<T>(String name, out T value)
        {
            return _context.TryGet(name, out value);
        }
        public Boolean TryGet<T>(out T value)
        {
            return _context.TryGet(out value);
        }
    }
}
