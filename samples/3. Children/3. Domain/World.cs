using Aggregates;
using Language;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Domain
{
    [Versioned("World", "Language")]
    public class World : Aggregates.Entity<World, WorldState>
    {
        private World() { }
        
        public void Create()
        {
            Apply<WorldCreated>(x => { });
        }
    }
}
