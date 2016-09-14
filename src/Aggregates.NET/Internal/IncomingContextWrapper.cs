using Aggregates.Contracts;
using NServiceBus.Pipeline.Contexts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Internal
{
    public class IncomingContextWrapper : IIncomingContextAccessor
    {
        private readonly IncomingContext _context;

        public IncomingContextWrapper(IncomingContext context)
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

        public object IncomingLogicalMessageInstance
        {
            get
            {
                return _context.IncomingLogicalMessage.Instance;
            }
        }

        public Type IncomingLogicalMessageMessageType
        {
            get
            {
                return _context.IncomingLogicalMessage.MessageType;
            }
        }

        public byte[] PhysicalMessageBody
        {
            get
            {
                return _context.PhysicalMessage.Body;
            }
        }
        public Address PhysicalMessageReplyToAddress
        {
            get
            {
                return _context.PhysicalMessage.ReplyToAddress;
            }
        }
        public String PhysicalMessageCorrelationId
        {
            get
            {
                return _context.PhysicalMessage.CorrelationId;
            }
        }
        public String PhysicalMessageId
        {
            get
            {
                return _context.PhysicalMessage.Id;
            }
        }

        public void SetPhysicalMessageHeader(String key, String value)
        {
            _context.PhysicalMessage.Headers[key] = value;
        }
        public IDictionary<String, String> PhysicalMessageHeaders
        {
            get
            {
                return _context.PhysicalMessage.Headers;
            }
        }
       

        public MessageIntentEnum PhysicalMessageMessageIntent
        {
            get
            {
                return _context.PhysicalMessage.MessageIntent;
            }
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
        public object MessageHandlerInstance
        {
            get
            {
                return _context.MessageHandler.Instance;
            }
        }
        public Boolean HandlerInvocationAborted
        {
            get
            {
                return _context.HandlerInvocationAborted;
            }
        }
    }
}
