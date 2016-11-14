
`These examples require an event store instance running with the config found in the samples directory`

Purpose
=======
The purpose of this sample is to display the out od band (OOB) events.  An OOB event is an event that is saved to the event store, but NOT retrived when hydrating the event from the store in the future.  The example models a very simple trader system in which trades are sent into the domain and after verified are recorded as an OOB event.  The advantage of such an event is its insignificance to the entity's job of verifying trades and therefore doesn't need to be pulled from the store each and every time we verify a new trade.

Included here are 3 projects which run simultaniously they are described below.
To run this example you must have RabbitMq installed - in the App.config its assumed RabbitMq is running on localhost but you can change to fit your needs.
If you have an NServiceBus license be sure that you've properly installed it so the programs can find it, otherwise manually add `config.LicensePath` to each endpoint.  At this point a license is not required you'll just get a bunch of annoying messages about it.

Domain
------
Contains the command handler and aggregate root, verifies trades 

Trader
-----
Sends trade commands to the domain instance

FrontDesk
-----
Prints trades processed


