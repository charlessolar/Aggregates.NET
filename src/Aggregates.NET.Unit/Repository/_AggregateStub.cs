using Aggregates.Contracts;
using NServiceBus.ObjectBuilder.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.Repository
{
    public class _AggregateStub : Aggregate<Guid>
    {
        private _AggregateStub() { }

        public void Fake()
        {
            Apply<CreateFake>(e => { });
        }

        private void Handle(CreateFake @event)
        {
        }

    }
}
