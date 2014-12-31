[![Build status](https://ci.appveyor.com/api/projects/status/r75p0yn5uo6colgk?svg=true)](https://ci.appveyor.com/project/volak/aggregates-net)

Aggregates.NET
==============

Aggregates.NET is a framework to help developers integrate the excelent [NServicebus](https://github.com/Particular/NServiceBus) and [NEventStore](https://github.com/NEventStore/NEventStore) libraries together.

Other libraries with similar goals:

- [NES](https://github.com/elliotritchie/NES)
- [CommonDomain](https://github.com/NEventStore/NEventStore/tree/master/src/NEventStore/CommonDomain)
- [DDD-CQRS-ES-Example](https://github.com/dcomartin/DDD-CQRS-ES-Example)

This project was originally inspired by and still uses many ideas from NES and CommonDomain.  

What will Aggregates.NET do for you?
------------------------------------

We fill in the gap between NEventStore and NServicebus.  Commands from NServicebus are applied to aggregate objects via message handlers and events generated via the aggregates are saved to the event stream and published to the bus.

Current features include -

- Aggregate root
- Aggregate defined by **any** type of Id (that can be converted to a string)
- Type safe Unit of Work and Repository pattern
- Automatic NServicebus and NEventstore configuration
- Automatic saving and publishing of domain events
- Message idempotency (depending on your storage choice)
- RavenDB persistance handlers
- NO internal IOC container (NServicebus used for resolutions)
- [Thorough sample](https://github.com/volak/DDD.Enterprise.Example)

Planned future features -

- GetEventStore support
- Automatic conflict resolution (when possible)
- Entities with automatic event registration and routing
- Specifications
- Business rules

Status
------

Aggregates.NET will handle retreiving roots from the event store and updating the stream after finishing command processing.  It can take your snapshots for you, and is very extendable.  Support for various nicities such as entities and value objects will be added in the near future.  There is a working example code base available above, which uses RavenDB for persistance.  I am currently waiting on the availability of two pull requests I've submitted to NEventstore to make an official build of Aggregates.NET.
I am also still designing and testing various features and have yet to put the entire project under test.  This process means some features/api might change so consider the project in an Alpha state

Nuget
-----

Code will be released on Nuget once the library is fully under test and we can verify everything works as planned.

Documentation
-------------

Coming soon!