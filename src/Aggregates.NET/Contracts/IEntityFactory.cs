using System;
using System.Collections.Generic;
using System.Text;
using Aggregates.Internal;
using Aggregates.Messages;

namespace Aggregates.Contracts
{
    interface IEntityFactory<TEntity> where TEntity : IEntity
    {
        TEntity Create(string bucket, Id id, IParentDescriptor[] parents = null, IEvent[] events = null, object snapshot = null);
    }
}
