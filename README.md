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
- Value Objects
- Specifications
- Support IDs of **any** type (that can be converted to a string)
- Type safe Unit of Work and Repository pattern
- Automatic saving and publishing of domain events
- Message idempotency
- NO internal IOC container (NServicebus used for resolutions)
- [Thorough sample](https://github.com/volak/DDD.Enterprise.Example)

Planned future features -

- Automatic conflict resolution (when possible)
- Automatic validation using specifications
- Projections configuration

Status
------

Aggregates.NET packages are starting to get published to the world.  The project is completely under test and can be integrated into NServicebus and Eventstore seemlessly.  Future versions of Aggregates.NET will be automatically published so keep checking back for latest features!

Nuget
-----

Nuget packages are published in a pre-release state.  They are available under the id Aggregates.NET.  There are also binaries and source code releases available via github.

Documentation
-------------

* [Wiki](https://github.com/volak/Aggregates.NET/wiki)
* [Example](https://github.com/volak/DDD.Enterprise.Example/)
