using Aggregates.Contracts;
using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Aggregates.Internal
{
    public class FullEventFactory
    {
        public static IFullEvent Event(IVersionRegistrar versionRegistry, Aggregates.UnitOfWork.IDomainUnitOfWork uow, IEntity entity, IEvent @event)
        {
            var eventId = Internal.UnitOfWork.NextEventId(uow.CommitId);

            var newEvent = new FullEvent
            {
                Descriptor = new EventDescriptor
                {
                    EntityType = versionRegistry.GetVersionedName(entity.GetType()),
                    StreamType = StreamTypes.Domain,
                    Bucket = entity.Bucket,
                    StreamId = entity.Id,
                    Parents = getParents(versionRegistry, entity),
                    Timestamp = DateTime.UtcNow,
                    Version = entity.StateVersion,
                    Headers = new Dictionary<string, string>(uow.CurrentHeaders) {
						[$"{Defaults.PrefixHeader}.{Defaults.OriginatingMessageId}"] = uow.MessageId.ToString(),
						[$"{Defaults.PrefixHeader}.{Defaults.CommitIdHeader}"] = uow.CommitId.ToString(),
                        [$"{Defaults.PrefixHeader}.{Defaults.EventIdHeader}"] = eventId.ToString(),
                    }
                },
                EventId = eventId,
                Event = @event
            };
            return newEvent;
        }

        private static IParentDescriptor[] getParents(IVersionRegistrar versionRegistry, IEntity entity)
        {
            if (entity == null)
                return null;
            if (!(entity is IChildEntity))
                return null;

            var child = entity as IChildEntity;

            var parents = getParents(versionRegistry, child.Parent)?.ToList() ?? new List<IParentDescriptor>();
            parents.Add(new ParentDescriptor { EntityType = versionRegistry.GetVersionedName(child.Parent.GetType()), StreamId = child.Parent.Id });
            return parents.ToArray();
        }
    }
}
