using Aggregates;
using Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    [Versioned("ParentEntity", "Samples")]
    public class ParentEntity : Aggregates.Entity<ParentEntity, ParentEntityState>
    {
        private ParentEntity() { }


        public void Name(string name)
        {
            Apply<ParentNamed>(x =>
            {
                x.Timestamp = DateTime.Now;
                x.Name = name;
            });
        }
    }

    [Versioned("ParentEntityState", "Samples")]
    public class ParentEntityState : Aggregates.State<ParentEntityState>
    {
        public string Name { get; private set; } = default!;

        private void Handle(ParentNamed e)
        {
            Name = e.Name;
        }
    }
}
