using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared
{
    public class MessageTotal : Aggregates.Messages.IMessage
    {
    }
    public interface MessageTotalResponse : Aggregates.Messages.IMessage
    {
        public int Total { get; set; }
    }
}
