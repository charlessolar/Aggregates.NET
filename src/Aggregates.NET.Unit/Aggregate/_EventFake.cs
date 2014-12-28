using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.Aggregate
{
    public class CreatedEvent
    {
        public Guid Id { get; set; }
        public String BucketId { get; set; }
        public String Value { get; set; }
    }

    public class UpdatedEvent
    {
        public String Value { get; set; }
    }
}
