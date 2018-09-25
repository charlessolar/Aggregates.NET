using Aggregates;
using Language;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain
{
    [Versioned("World", "Samples")]
    public class World : Aggregates.Entity<World, WorldState>
    {
        private World() { }

        public void SayHello(string message)
        {
            Rule("Repetition", x => x.LastMessage == message, "Don't repeat yourself");

            Apply<SaidHello>(x =>
            {
                x.Message = message;
            });
        }
    }

    public class WorldState : Aggregates.State<WorldState>
    {
        public string LastMessage { get; private set; }

        private void Handle(SaidHello e)
        {
            LastMessage = e.Message;
        }
    }
}
