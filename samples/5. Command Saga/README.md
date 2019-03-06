# Command Saga

When you need a set of commands to be run in a series - waiting for each to be accepted before starting the next one - use a Saga.  This example shows how to start a command saga after receiving an event. 


Internally we use a NServiceBus saga to keep track of the commands executing - we also keep track of the originating message and any "abort" commands should the chain of commands fail.  If the series of commands does fail, we first send the abort commands then send the original message to the error queue.

Consider a task to seed an example customer entity with a few sales:

```
public Task Handle(Events.CustomerSeed e, IMessageHandlerContext ctx) 
{

	var saga = ctx.Saga(e.CustomerID)
		.Command(new CustomerCommands.Create {
			CustomerId = e.CustomerId,
			Name = e.Name
		})
		.Command(new CustomerCommands.Activate {
			CustomerId = e.CustomerId
		});

	if(e.Sales.Any())
		foreach(var sale in e.Sales)
			saga.Command(new OrderCommands.CreateSale {
				CustomerId = e.CustomerId,
				Item = sale.ItemId,
				Quantity = sale.Quantity,
				Price = sale.Price
			});

	saga.OnAbort(new CustomerCommands.SetException {
		CustomerId = e.CustomerId
	});

	return saga.Start();

}
```

This will start a saga which will create a customer, activate it, and create a few sales orders (if any exist) - in that order, waiting for each command to be accepted.  If any command fails in the chain - the saga will set the customer into an "Exception" state via the `OnAbort` command.
