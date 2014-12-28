using Aggregates.Contracts;
using NServiceBus.ObjectBuilder.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.Aggregate
{
    public class _AggregateStub : Aggregate<Guid>
    {
        public String Value { get; set; }

        public _AggregateStub(IContainer container) : base(container) { }


        public void Handle(CreatedEvent @event)
        {
            this.Value = @event.Value;
        }
        public void Handle(UpdatedEvent @event)
        {
            this.Value = @event.Value;
        }

        public void ThrowEvent(String value)
        {
            Apply<UpdatedEvent>(e =>
            {
                e.Value = value;
            });
        }

    }
}
