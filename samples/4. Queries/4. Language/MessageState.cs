using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Language
{
    public class MessageState : Aggregates.State<MessageState>
    {
        public string Message { get; private set; }

        private void Handle(SaidHello e)
        {
            Message = e.Message;
        }
    }
}
