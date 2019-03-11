using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class TrackChildren : ITrackChildren
    {
        private string _endpoint;
        private Version _version;

        private readonly IEventStoreConsumer _consumer;
        private readonly IStoreEvents _eventstore;
        private readonly IVersionRegistrar _registrar;

        public TrackChildren(IEventStoreConsumer consumer, IStoreEvents eventstore, IVersionRegistrar registrar)
        {
            _consumer = consumer;
            _eventstore = eventstore;
            _registrar = registrar;
        }

        private IParentDescriptor[] getParents(IEntity entity)
        {
            if (entity == null)
                return null;

            var parents = getParents((entity as IChildEntity)?.Parent)?.ToList() ?? new List<IParentDescriptor>();
            parents.Add(new ParentDescriptor { EntityType = _registrar.GetVersionedName(entity.GetType()), StreamId = entity.Id });
            return parents.ToArray();
        }
        public async Task Setup(string endpoint, Version version)
        {
            _endpoint = endpoint;
            // Changes which affect minor version require a new projection, ignore revision and build numbers
            _version = new Version(version.Major, version.Minor);

            // Todo: is it necessary to ensure JSON.parse works?
            // everything in DOMAIN should be from us - so all metadata will be parsable


            // this projection will parse PARENTS metadata in our events and create partitioned states representing all the CHILDREN
            // of an entity.

            // Don't tab this '@' will create tabs in projection definition
            var definition = @"
options({{
    $includeLinks: false,
    reorderEvents: false,
    processingLag: 0
}});

fromCategory('{0}')
.partitionBy(function(event) {{
    let metadata = JSON.parse(event.metadataRaw);
    if(metadata.Parents === null || metadata.Parents.length === 0)
        return undefined;
    let lastParent = metadata.Parents.pop();
        
    let streamId = 'CHILDREN' + '-' + metadata.Bucket + '-[' + metadata.Parents.join(':') + ']-' + lastParent.EntityType + '-' + lastParent.Id;
        
    return streamId;
}})
.when({{
    $init: function() {{
        return {{
            Children: []
        }};
    }},
    $any: function(state, event) {{
        let metadata = JSON.parse(event.metadataRaw);
        if(metadata.Version !== 0)
            return state;
            
        state.Children.push({{ EntityType: metadata.EntityType, StreamId: metadata.StreamId }});
        return state;
    }}
}})
.outputState();";

            var appDefinition = string.Format(definition, StreamTypes.Domain);
            await _consumer.CreateProjection($"aggregates.net.children.{_version}", appDefinition).ConfigureAwait(false);
        }
        public async Task<TEntity[]> GetChildren<TEntity, TParent>(TParent parent) where TEntity : IChildEntity<TParent> where TParent : IHaveEntities<TParent>
        {
            var uow = (Configuration.Settings.LocalContainer.Value ?? Configuration.Settings.Container).Resolve<Aggregates.UnitOfWork.IDomain>();
            var streamGen = Configuration.Settings.Generator;

            var parents = getParents(parent);

            var childEntityType = _registrar.GetVersionedName(typeof(TEntity));
            var stream = streamGen(childEntityType, StreamTypes.Children, parent.Bucket, parent.Id, parents?.Select(x => x.StreamId).ToArray());

            // ES generated stream name
            var fullStream = $"$projections-aggregates.net.children.{_version}-{stream}-result";

            var stateEvents = await _eventstore.GetEventsBackwards(fullStream, count: 1).ConfigureAwait(false);
            if (!stateEvents.Any())
                return new TEntity[] { };

            var state = stateEvents[0];
            var children = state.Event as ChildrenProjection;
            if (children == null || !children.Children.Any())
                return new TEntity[] { };

            var entities = new List<TEntity>();
            foreach (var child in children.Children.Where(x => x.EntityType == childEntityType))
            {
                var childEntity = await uow.For<TEntity, TParent>(parent).Get(child.StreamId).ConfigureAwait(false);
                entities.Add(childEntity);
            }
            return entities.ToArray();
        }

    }
}
