[![Build status](https://ci.appveyor.com/api/projects/status/r75p0yn5uo6colgk?svg=true)](https://ci.appveyor.com/project/volak/aggregates-net)

Aggregates.NET
==============

Aggregates.NET is a framework to help developers integrate the excellent [NServicebus](https://github.com/Particular/NServiceBus) and [EventStore](https://github.com/EventStore/EventStore) libraries together.

This library contains code to help create and manage domain driven design objects such as Aggregates, Entities, Value Objects, etc.  This framework is by far not the only option, other libraries include:

- [NES](https://github.com/elliotritchie/NES)
- [CommonDomain](https://github.com/NEventStore/NEventStore/tree/master/src/NEventStore/CommonDomain)
- [DDD-CQRS-ES-Example](https://github.com/dcomartin/DDD-CQRS-ES-Example)
- [Eventful](https://github.com/adbrowne/Eventful)
- [SimpleDomain](https://github.com/froko/SimpleDomain)

This project was originally inspired by and still uses many ideas from NES and CommonDomain.  

What will Aggregates.NET do for you?
------------------------------------

We fill in the gap between EventStore and NServicebus.  Commands from NServicebus are applied to aggregate objects via message handlers and events generated via the aggregates are saved to the event stream and published to the bus.

Current features include -

- Aggregate Roots
- Entities
- Children entities of entities (infinite parenthood)
- Value Objects
- Snapshotting
- Specifications
- Multithreaded event dispatching
- Type safe Unit of Work and Repository pattern
- Automatic saving and publishing of domain events
- Out of band events (events saved outside of an object's event stream)
- Scalable event consumers via competing consumer
- Async event and message handling (in NSB 5!)
- Message idempotency
- Automatic conflict resolution (when possible)
- NO internal IOC container (NServicebus used for resolutions)
- [Thorough sample](https://github.com/volak/DDD.Enterprise.Example)


Status
------

Aggregates.NET is still under development but I personally am using it in 2 projects so its very usable.  Expect fairly often updates via Nuget as I tend to add and fix things when the issue pops up.  Sometimes the packages have a bug or some small issue but I always fix it right away. 
I do not have any plans yet for 'stable' releases so only use the library is you are comfortable with beta builds.

Nuget
-----

Nuget packages are published in a pre-release state.  They are available under the id Aggregates.NET.  There are also binaries and source code releases available via github.

Documentation
-------------

* [Wiki](https://github.com/volak/Aggregates.NET/wiki)
* [Example](https://github.com/volak/DDD.Enterprise.Example/)
