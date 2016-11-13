
`These examples require an event store instance running with the config found in the samples directory`

Purpose
=======
The purpose of this sample is to display the delayed event/command feature.  Which allows you to process commands and events in bulk - this allows you to better utilize caching to process a ton of events or commands faster.

Included here are 3 projects which run simultaniously they are described below.
To run this example you must have RabbitMq installed - in the App.config its assumed RabbitMq is running on localhost but you can change to fit your needs.
If you have an NServiceBus license be sure that you've properly installed it so the programs can find it, otherwise manually add `config.LicensePath` to each endpoint.  At this point a license is not required you'll just get a bunch of annoying messages about it.

Domain
------
Contains the command handler and aggregate root

Hello
-----
Sends commands to the domain instance, has two modes - individual and bulk

When its run it will ask for a username then allow you to enter any message.  The message will be sent 1000 times in single mode, then 1000 times using the bulk delayed processing 

World
-----
Prints how many messages were "processed" and the time it took for both sets of events from Domain


