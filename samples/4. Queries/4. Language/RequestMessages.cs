using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Language
{
    public class RequestMessages : IMessage
    {
    }
    public class MessagesResponse : IMessage
    {
        public string[] Messages { get; set; }
    }
}
