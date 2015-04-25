using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IEntity : IEventSource, IEventRouter
    {
    }

    public interface IEntity<TId, TAggregateId> : IEntity, IEventSource<TId>
    {
        TAggregateId AggregateId { get; set; }
    }
}