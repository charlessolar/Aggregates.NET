using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Language
{

    public class WorldState : Aggregates.State<WorldState>
    {
        public string[] Previous { get; private set; }

        private void Handle(SaidHello e)
        {
            Previous = Previous.Concat(new[] { e.Message }).ToArray();
        }

        protected override bool ShouldSnapshot()
        {
            // Take a snapshot every 3 events
            return this.Version - (this.Snapshot?.Version ?? 0) >= 3;
        }
    }
}
