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
            Apply<Event>(x =>
            {
                x.User = this.Id;
                x.Message = Message;
            });
        }

    }
}
