
`These examples require an event store instance running with the config found in the samples directory`

Purpose
=======
The purpose of this sample is to demonstrait the kinds of performance counters Aggregates.NET contains internally that you can use in your own apps to get a peak into how well your application is performing and to check for issues.  Aggregates.NET uses Metrics.NET to 
publish performance counters which can be viewed in the system app `Performance Monitor` or via a helpful web site Metrics.NET makes available.  The web site is enabled on all 3 endpoints currently, port information is below.

Included here are 3 projects which run simultaniously they are described below.
To run this example you must have RabbitMq installed - in the App.config its assumed RabbitMq is running on localhost but you can change to fit your needs.
If you have an NServiceBus license be sure that you've properly installed it so the programs can find it, otherwise manually add `config.LicensePath` to each endpoint.  At this point a license is not required you'll just get a bunch of annoying messages about it.

Domain
------
Contains the command handler and aggregate root

Metrics.NET web portal is running at `http://localhost:1000`

Sender
-----
Sends demo commands to the domain instance 

Metrics.NET web portal is running at `http://localhost:1111`

Receiver
-----
Reads events from eventstore - doesn't do anything with them

Metrics.NET web portal is running at `http://localhost:1222`