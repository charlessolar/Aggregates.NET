using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared;

namespace Domain
{
    class User : Aggregates.Aggregate<User>
    {
        private User()
        {
        }

        public void Create()
        {
            Apply<NewUser>(x =>
            {
                x.User = this.Id;
            });
        }

        public void SayHello(String Message)
        {
            Raise<SaidHello>(x =>
            {
                x.User = this.Id;
                x.Message = Message;
            });
        }
        public void SayHelloALot(String Message)
        {
            Raise<SaidHelloALot>(x =>
            {
                x.User = this.Id;
                x.Message = Message;
            });
        }

        public void StartHello(DateTime Timestamp)
        {
            Apply<StartedHello>(x =>
            {
                x.User = this.Id;
                x.Timestamp = Timestamp;
            });
        }

        public void EndHello(DateTime Timestamp)
        {
            Apply<EndedHello>(x =>
            {
                x.User = this.Id;
                x.Timestamp = Timestamp;
            });
        }
    }
}
