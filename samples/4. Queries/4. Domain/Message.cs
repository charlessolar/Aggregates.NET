using Language;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class Message : Aggregates.Entity<Message, MessageState, World>
    {
        private Message() { }

        public void SayHello(string message)
        {
            Apply<SaidHello>(x =>
            {
                x.MessageId = Id;
                x.Message = message;
            });
        }
    }

}
