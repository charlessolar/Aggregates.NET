using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Internal
{
    [Versioned("ChildrenProjection", "Aggregates")]
    public class ChildrenProjection : IEvent
    {
        public class ChildDescriptor
        {
            public string EntityType { get; set; }
            public Id StreamId { get; set; }
        }
        

        public ChildDescriptor[] Children { get; set; }
    }
}
