using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared
{
    public class MessageTotal : IMessage
    {
    }
    public interface MessageTotalResponse : IMessage
    {
        public int Total { get; set; }
    }
}
