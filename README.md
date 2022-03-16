|              |                                                                                                                                                                                    |
| ------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Build**    | [![Build status](https://ci.appveyor.com/api/projects/status/r75p0yn5uo6colgk?svg=true&branch=master)](https://ci.appveyor.com/project/charlessolar/aggregates-net)                |
| **Coverage** | [![Coverage Status](https://coveralls.io/repos/github/charlessolar/Aggregates.NET/badge.svg?branch=master)](https://coveralls.io/github/charlessolar/Aggregates.NET?branch=master) |
| **Quality**  | [![GitHub issues](https://img.shields.io/github/issues-raw/charlessolar/aggregates.net.svg)](https://github.com/charlessolar/Aggregates.NET/issues)                                |
| **Nuget**    | [![Nuget](https://buildstats.info/nuget/Aggregates.NET)](http://nuget.org/packages/Aggregates.NET)                                                                                 |

# Aggregates.NET v0.17

Aggregates.NET is a library to facilitate integration between [NServiceBus](https://github.com/Particular/NServiceBus) and [EventStore](https://github.com/EventStore/EventStore). It provides a framework to define entities, value objects, command and event handlers, and many other domain driven design and CQRS principles. The framework should take all the tediousness of dealing with event streams and message queues out of your consideration and give you a solid base to build a solid event sourced application.

## What will Aggregates.NET do for you?

Take the following example

```
class Handler :
    IHandleMessages<Send>
{
    public async Task Handle(Send command, IMessageHandlerContext ctx)
    {
        var entity = await ctx.For<EchoEntity>().TryGet("default");
        if (entity == null)
            entity = await ctx.For<EchoEntity>().New("default");

        entity.Echo(command.Message);
    }
} 
```

Users of NSB should immediately notice the `IHandleMessages` convention - everything inside the message handler is provided by Aggregates.NET. The special extension methods are using entity definitions to retreive streams from the eventstore. Once loaded the entity executes a method which generates an event which Aggregates.NET will save to the eventstore. 

All of the complicated stream writing / reading details are handled internally by Aggregates.NET leaving you free to design your contexts and business objects without caring about the transport or storage.

#### [More samples are located here](https://github.com/charlessolar/Aggregates.NET/tree/master/samples)


More features of Aggregates.NET include -

- Children entities of entities (infinite parenthood)
- Snapshotting
- Sagas (similar to but easier to define than NSB's)
- Queries
- Unit of Work management for application storage (MongoDB, eventstore, etc)

## Versioning

v1.0 is coming soon<sup>tm</sup>! As of March 2022 I just completed a very big overhaul of Aggregates.NET replacing the many container implementations and logging with Microsoft's standard libraries. I also cleaned up some more complicated bits of code to streamline the purpose the library over needless features.

I expect v1.0 to come sometime over the next year and with that a stable API which I will do my very best to maintain.

This is however a solo project so I make no guarantees that somethings may break over time. Maintaining backwards compatibility is not at the top of my priorities but if you are using Aggregates.NET and wish to contract my work for maintenance or development feel free to contact me via email!

## Documentation

This is a one man project so documentation is lacking - sorry about that. If you have any questions about using Aggregates.NET feel free to contact me via email.

- [Wiki](https://github.com/charlessolar/Aggregates.NET/wiki)
- [Simple Examples](https://github.com/charlessolar/Aggregates.NET/tree/master/samples)
- [TodoMVC Style Example (recommended)](https://github.com/charlessolar/TodoMVC-DDD-CQRS-EventSourcing)
