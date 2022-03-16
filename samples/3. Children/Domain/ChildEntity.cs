using Aggregates;
using Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    [Versioned("ChildEntity", "Samples")]
    public class ChildEntity : Aggregates.Entity<ChildEntity, ChildEntityState, ParentEntity>
    {
        private ChildEntity() { }

        public void Name(string name)
        {
            Apply<ChildNamed>(x =>
            {
                x.Timestamp = DateTime.Now;
                // Children can access parent state like so
                x.Parent = Parent.State.Name;
                x.Name = name;
            });
        }
    }
    [Versioned("ChildEntityState", "Samples")]
    public class ChildEntityState : Aggregates.State<ChildEntityState, ParentEntityState>
    {
        public string Name { get; private set; } = default!;

        private void Handle(ChildNamed e)
        {
            Name = e.Name;
        }
    }
}
