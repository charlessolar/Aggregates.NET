# Children

One unique feature of Aggregates.NET is the ability to load entities as children of a parent entity.  These entities are stored in the eventstore with properly recorded parents heirarchy.

```
public void Handle(VoidInvoiceLine command, IMessageHandlerContext ctx) 
{
	var invoice = ctx.For<Invoice>().Get(command.InvoiceId);
	var line = invoice.For<Line>().Get(command.LineId);

	line.Void();
}
```

