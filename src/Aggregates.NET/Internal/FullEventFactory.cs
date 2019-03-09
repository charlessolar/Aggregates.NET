using Aggregates.Contracts;
using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Aggregates.Internal
{
    public class FullEventFactory
    {
        public static IFullEvent OOBEvent(IVersionRegistrar versionRegistry, Aggregates.UnitOfWork.IDomain uow, IEntity entity, IEvent @event, string id, bool transient, int? daysToLive)
        {
            var eventId = Internal.UnitOfWork.NextEventId(uow.CommitId);

            var newEvent = new FullEvent
            {
                Descriptor = new EventDescriptor
                {
                    EntityType = versionRegistry.GetVersionedName(entity.GetType()),
                    StreamType = StreamTypes.OOB,
                    Bucket = entity.Bucket,
                    StreamId = entity.Id,
                    Parents = getParents(versionRegistry, entity),
                    Timestamp = DateTime.UtcNow,
                    Version = entity.StateVersion,
                    Headers = new Dictionary<string, string>()
                    {
                        [$"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}"] = eventId.ToString(),
                        [$"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}"] = uow.CommitId.ToString(),
                        [Defaults.OobHeaderKey] = id,
                        [Defaults.OobTransientKey] = transient.ToString(),
                        [Defaults.OobDaysToLiveKey] = daysToLive.ToString()
                    }
                },
                EventId = eventId,
                Event = @event
            };

            return newEvent;
        }
        public static IFullEvent Event(IVersionRegistrar versionRegistry, Aggregates.UnitOfWork.IDomain uow, IEntity entity, IEvent @event)
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
                    Headers = new Dictionary<string, string>()
                    {
                        [$"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}"] = eventId.ToString(),
                        [$"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}"] = uow.CommitId.ToString(),
                    }
                },
                EventId = eventId,
                Event = @event
            };
            return newEvent;
        }

        private static IParentDescriptor[] getParents(IVersionRegistrar versionRegistry, IEntity entity)
        {
            if (!(entity is IChildEntity))
                return null;

            var parents = getParents(versionRegistry, (entity as IChildEntity).Parent)?.ToList() ?? new List<IParentDescriptor>();
            parents.Add(new ParentDescriptor
            {
                EntityType = versionRegistry.GetVersionedName(entity.GetType()),
                Id = entity.Id
            });
            return parents.ToArray();
        }
    }
}
