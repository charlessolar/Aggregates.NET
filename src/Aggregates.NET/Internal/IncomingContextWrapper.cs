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
    public class IncomingContextWrapper : IContextAccessor
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

        public string PhysicalMessageId
        {
            get
            {
                return _context.PhysicalMessage.Id;
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
    }
}
