using Aggregates.Contracts;
using Aggregates.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    public class EventStoreConsumer : IEventStoreConsumer
    {
        private readonly Microsoft.Extensions.Logging.ILogger Logger;

        private readonly IMetrics _metrics;
        private readonly IVersionRegistrar _registrar;
        private readonly IEventStoreClient _client;

        private readonly StreamIdGenerator _streamIdGen;
        private readonly bool _allEvents;

        public EventStoreConsumer(ILogger<EventStoreConsumer> logger, IMetrics metrics, ISettings settings, IVersionRegistrar registrar, IEventStoreClient client)
        {
            Logger = logger;
            _metrics = metrics;
            _registrar = registrar;
            _client = client;

            _streamIdGen = settings.Generator;
            _allEvents = settings.AllEvents;
        }

        public async Task SetupProjection(string endpoint, Version version, Type[] eventTypes)
        {

            // Dont use "-" we dont need category projection projecting our projection
            var stream = $"{endpoint}.{version}".Replace("-", "");

            await _client.EnableProjection("$by_category").ConfigureAwait(false);
            // Link all events we are subscribing to to a stream
            var functions = !eventTypes.Any() ? "" :
                eventTypes
                    .Select(
                        eventType => $"'{_registrar.GetVersionedName(eventType)}': processEvent")
                    .Aggregate((cur, next) => $"{cur},\n{next}");

            // endpoint will get all events regardless of version of info
            // it will be up to them to handle upgrades
            if (_allEvents)
                functions = "$any: processEvent";

            // No watched events, no projection
            if (string.IsNullOrEmpty(functions))
                return;

            // Don't tab this '@' will create tabs in projection definition
            var definition = @"
function processEvent(s,e) {{
    linkTo('{1}', e);
}}
fromCategories([{0}]).
when({{
{2}
}});";

            Logger.DebugEvent("Setup", "Setup event projection");
            var appDefinition = string.Format(definition, $"'{StreamTypes.Domain}'", stream, functions);
            await _client.CreateProjection($"{stream}.app.projection", appDefinition).ConfigureAwait(false);
        }
        public async Task SetupChildrenProjection(string endpoint, Version version)
        {

            // Todo: is it necessary to ensure JSON.parse works?
            // everything in DOMAIN should be from us - so all metadata will be parsable


            // this projection will parse PARENTS metadata in our events and create partitioned states representing all the CHILDREN
            // of an entity.

            // Don't tab this '@' will create tabs in projection definition
            var definition = @"
options({{
    $includeLinks: false
}});

function createParents(parents) {{
    if(!parents || !parents.length || parents.length === 0)
        return '';

    return parents.map(function(x) {{ return x.StreamId; }}).join(':');
}}

fromCategory('{0}')
.partitionBy(function(event) {{
    let metadata = JSON.parse(event.metadataRaw);
    if(metadata.Parents === null || metadata.Parents.length == 0)
        return undefined;
    let lastParent = metadata.Parents.pop();
        
    let streamId = '{1}' + '-' + metadata.Bucket + '-[' + createParents(metadata.Parents) + ']-' + lastParent.EntityType + '-' + lastParent.StreamId;
        
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

            Logger.DebugEvent("Setup", "Setup children tracking projection [{Name}]", $"aggregates.net.children.{version}");
            var appDefinition = string.Format(definition, StreamTypes.Domain, StreamTypes.Children);
            await _client.CreateProjection($"aggregates.net.children.{version}", appDefinition).ConfigureAwait(false);
        }

        private IParentDescriptor[] getParents(IEntity entity)
        {
            if (entity == null)
                return null;
            if (!(entity is IChildEntity))
                return null;

            var child = entity as IChildEntity;

            var parents = getParents(child.Parent)?.ToList() ?? new List<IParentDescriptor>();
            parents.Add(new ParentDescriptor { EntityType = _registrar.GetVersionedName(child.Parent.GetType()), StreamId = child.Parent.Id });
            return parents.ToArray();
        }
        public Task<ChildrenProjection> GetChildrenData<TParent>(Version version, TParent parent) where TParent : IHaveEntities<TParent>
        {
            var parents = getParents(parent);

            var parentEntityType = _registrar.GetVersionedName(typeof(TParent));

            Logger.DebugEvent("Children", "Getting children for entity type [{EntityType}] stream id [{StreamId}]", parentEntityType, parent.Id);

            var parentString = parents?.Select(x => x.StreamId).BuildParentsString();
            // Cant use streamGen setting because the projection is set to this format
            var stream = $"{StreamTypes.Children}-{parent.Bucket}-[{parentString}]-{parentEntityType}-{parent.Id}";
            return _client.GetProjectionResult<ChildrenProjection>($"aggregates.net.children.{version}", stream);
        }

        public async Task ConnectToProjection(string endpoint, Version version, IEventStoreConsumer.EventAppeared callback)
        {
            // Dont use "-" we dont need category projection projecting our projection
            var stream = $"{endpoint}.{version}".Replace("-", "");

            Logger.DebugEvent("Connect", "Connecting to event projection [{Stream}]", stream);
            var success = await _client.ConnectPinnedPersistentSubscription(stream, endpoint,
                (eventStream, eventNumber, @event) =>
                {
                    var headers = @event.Descriptor.Headers;
					headers[$"{Defaults.PrefixHeader}.EventType"] = @event.EventType;
                    // added in FullEventFactory
					//headers[$"{Defaults.PrefixHeader}.EventId"] = @event.EventId.ToString();
                    headers[$"{Defaults.PrefixHeader}.EventStream"] = eventStream;
                    headers[$"{Defaults.PrefixHeader}.EventPosition"] = eventNumber.ToString();

                    return callback(@event.Event, headers);
                });

            if (!success)
                throw new Exception($"Failed to connect to projection {stream}");
        }

    }
}
