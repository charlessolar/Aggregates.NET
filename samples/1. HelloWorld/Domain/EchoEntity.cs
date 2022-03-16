using Aggregates;
using Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    [Versioned("EchoEntity", "Samples")]
    public class EchoEntity : Aggregates.Entity<EchoEntity, EchoEntityState>
    {
        private EchoEntity() { }


        public void Echo(string message)
        {
            //Rule("Repetition", x => x.LastMessage == message, "Don't repeat yourself");

            Apply<Echo>(x =>
            {
                x.Timestamp = DateTime.Now;
                x.Message = message;
            });
        }
    }

    public class EchoEntityState : Aggregates.State<EchoEntityState>
    {
        public string LastMessage { get; private set; } = default!;

        private void Handle(Echo e)
        {
            LastMessage = e.Message;
        }
    }
}
