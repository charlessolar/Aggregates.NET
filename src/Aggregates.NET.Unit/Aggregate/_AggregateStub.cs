using Aggregates.Contracts;
using NServiceBus.ObjectBuilder.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.Aggregate
{
    public class Memento : IMemento<Guid>
    {
        public Guid Id { get; set; }
        public String Value { get; set; }
    }

    public class _AggregateStub : AggregateWithMemento<Guid, Memento>
    {
        public Boolean SnapshotTaken { get; set; }
        public String Value { get; set; }

        public _AggregateStub(IContainer container) : base(container) { }

        protected override void RestoreSnapshot(Memento memento)
        {
            Value = memento.Value;
        }

        protected override Memento TakeSnapshot()
        {
            return new Memento { Value = Value };
        }

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
