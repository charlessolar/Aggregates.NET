
Purpose
=======
The purpose of this extremely simple sample is to get you up and running with Aggregates.NET, NServiceBus, RabbitMq, and Eventstore quickly.  Included here are 3 projects which run simultaniously they are described below.
To run this example you must have RabbitMq installed - in the App.config its assumed RabbitMq is running on localhost but you can change to fit your needs.
If you have an NServiceBus license be sure that you've properly installed it so the programs can find it, otherwise manually add `config.LicensePath` to each endpoint.  At this point a license is not required you'll just get a bunch of annoying messages about it.

Domain
------
Contains the command handler and aggregate root

Hello
-----
Sends commands to the domain instance

When its run it will ask for a username then allow you to enter any message you want which will be sent to the Domain and eventually make it over to the World instance.

World
-----
Prints the message Hello is sending