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
- Aggregate defined by **any** type of Id
- Type safe Unit of Work and Repository pattern
- Automatic NServicebus and NEventstore configuration
- Automatic publishing of domain events

Planned future features -

- Automatic conflict resolution (when possible)
- Entities with automatic event registration and routing
- Specifications
- Business rules

Status
------

Codebase will compile but there are no tests, over the next few weeks I'll be adding a test project and verifying that Aggregates.NET works as intended.  This process means some features/api might change so consider the project in an Alpha state

Nuget
-----

Code will be released on Nuget once the library is fully under test and we can verify everything works as planned.

Documentation
-------------

Coming soon!