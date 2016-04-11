using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public abstract class IHandleMessages<TMessage> : NServiceBus.IHandleMessages<TMessage>
    {
        void NServiceBus.IHandleMessages<TMessage>.Handle(TMessage message)
        {
            Task.Run(() => Handle(message)).Wait();
        }

        public abstract Task Handle(TMessage message);
    }
}
