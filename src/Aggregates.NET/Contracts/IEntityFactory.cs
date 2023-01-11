using Aggregates.Messages;
using Microsoft.Extensions.Logging;

namespace Aggregates.Contracts
{
    interface IEntityFactory<TEntity> where TEntity : IEntity
    {
        TEntity Create(ILogger Logger, string bucket, Id id, IParentDescriptor[] parents = null, IEvent[] events = null, object snapshot = null);
    }
}
