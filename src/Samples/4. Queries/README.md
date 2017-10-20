# Queries

A query is line a domain service - it answers a particular question with data.  Usually a query handler loads data from an external database like KV storage - but queries can be anything.

In this example the query is to get all previously sent messages.  When each message happens the event handler saves the message id into a list, and when the query is sent the endpoint uses the in-memory list to get all previous messages and return the result.

```
public async Task<MessageState[]> Handle(PreviousMessages query, IDomainUnitOfWork uow)
{
    // Can use uow to get domain entities
    var world = await uow.For<World>().TryGet("World");
    if (world == null)
        return new MessageState[] { };

    var states = new List<MessageState>();
    foreach(var id in MessageIds)
    {
        var message = await world.For<Message>().Get(id).ConfigureAwait(false);
        states.Add(message.State);
    }
    return states.ToArray();
}
```

The method signature is almost the same as a message handler, but in this case because there is no IMessageHandlerContext the IDomainUnitOfWork is passed right into the method.

You query for this data from a message handler like so:

```
var messages = await ctx.Query<PreviousMessages, MessageState[]>(new PreviousMessages()).ConfigureAwait(false);
```
