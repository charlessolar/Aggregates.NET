using Aggregates.Contracts;
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

        protected override void RestoreSnapshot(Memento memento)
        {
            Value = memento.Value;
        }

        protected override Memento TakeSnapshot()
        {
            return new Memento { Value = Value };
        }
    }
}
