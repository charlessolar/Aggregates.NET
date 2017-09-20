[![Build status](https://ci.appveyor.com/api/projects/status/r75p0yn5uo6colgk?svg=true)](https://ci.appveyor.com/project/volak/aggregates-net)

# Aggregates.NET

Aggregates.NET is a framework to help developers integrate the excellent [NServiceBus](https://github.com/Particular/NServiceBus) and [EventStore](https://github.com/EventStore/EventStore) libraries together.

This library contains code to help create and manage domain driven design objects such as Aggregates, Entities, Value Objects, etc.  This framework is by far not the only option, other libraries include:

- [NES](https://github.com/elliotritchie/NES)
- [CommonDomain](https://github.com/NEventStore/NEventStore/tree/master/src/NEventStore/CommonDomain)
- [DDD-CQRS-ES-Example](https://github.com/dcomartin/DDD-CQRS-ES-Example)
- [Eventful](https://github.com/adbrowne/Eventful)
- [SimpleDomain](https://github.com/froko/SimpleDomain)
- [NDomain](https://github.com/mfelicio/NDomain)

This project was originally inspired by and still uses many ideas from NES and CommonDomain.  

## Whats New?

I recently bumped the version of the project to v0.11 which involved a rewrite of much of the project.  To better facilitate including other message buses, eventstores, DI, or serialization the project is split into assemblies that include a certain dependency rather than assemblies that perform a certain function.

Also new are State objects and the removal of the aggregate root class type.  In Aggregates.NET aggregate roots were modeled as entities without parents, and entities were children of aggregates.  Now, everything is an entity - the concept of an aggregate root still exists but its not a separate class.
State objects represent the internal eventsourced state of an entity.  Upon loading an entity the state object is hydrated with the existing events and used by commands to certify commands.  [See it in action](https://github.com/volak/Aggregates.NET/blob/master/src/Samples/HelloWorld/Domain/World.cs)

## What will Aggregates.NET do for you?

We fill in the gap between EventStore and NServiceBus.  Commands from NServiceBus are applied to entity objects via message handlers and events generated via the entities are saved to the event stream.  We also maintain consumer projections for read model generation allowing the handling of commands and events using just `IHandleMessages<>`

Current features include -

- Entities
- Children entities of entities (infinite parenthood)
- Value Objects
- Snapshotting
- Query pattern
- Unit of Work and Repository pattern
- Automatic saving and reading of domain events
- Out of band events (events saved or published which do not affect business logic of entity)
- Bulk command and event delivery
- Intelligent and configurable conflict resolution
- Automatic configuration of projections and competing consumers for consumers
- EventStore sharding
- Automatic command accept/reject replies
- Ton of performance counters

## Performance

Aggregates.NET is not *slow* - but I did not write it focused on bleeding fast performance.  "Premature optimization is bad" etc etc.  Aggregates.NET is however designed with features meant to allow you to perform well.
A great example is the support for bulk command and event processing.  When setup you can have your app process say 1000 messages of a specific type at once instead of one at a time.  The advantage being that you can cache objects while processing saving a vast amount of read time from your database.  
These features of course have trade offs and should only be used in specialized circumstances but when your app is tuned correctly you'll definitely see greater throughput than a traditional `read, hydrate, write, repeat` paradigm.

Currently Aggregates.NET offers the following performance features:

- Snapshotting
- Bulk message (commands and events) delivery
- Special "weak" conflict resolver which delays stream conflict resolution preventing conflict hell
- Smart snapshot store
- Out of band events
- Async throughout

## Status

Aggregates.NET is still under development but I personally am using it in 2 projects so its very usable.  Expect fairly often updates via Nuget as I tend to add and fix things when the issue pops up.  Sometimes the packages have a bug or some small issue but I always fix it right away. 
I do not have any plans yet for 'stable' releases so only use the library is you are comfortable with beta builds.

I have no plans to freeze the API or do semantic versioning anytime soon - so keep that in mind when updating packages

## Other Transports / EventStores

I welcome pull requests for other transports or stores - otherwise they'll only be added if I need them


## Nuget

Nuget packages are published in a pre-release state.  They are available under the id Aggregates.NET.  There are also binaries and source code releases available via github.

## Documentation

This is a one man project so documentation is lacking - sorry about that.  If you have any questions about using Aggregates.NET feel free to contact me via email or slack (username charles) ([the ddd/cqrs slack group](https://ddd-cqrs-es.herokuapp.com/))

* [Wiki](https://github.com/volak/Aggregates.NET/wiki)
* [Simple Examples](https://github.com/volak/Aggregates.NET/tree/master/src/Samples)
* [Enterprise Example](https://github.com/volak/DDD.Enterprise.Example/)

