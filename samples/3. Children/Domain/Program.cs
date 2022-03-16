using Aggregates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NServiceBus;
using Shared;


var endpointConfiguration = new EndpointConfiguration("Domain");
endpointConfiguration.UsePersistence<LearningPersistence>();
endpointConfiguration.UseTransport<LearningTransport>();
endpointConfiguration.Pipeline.Register(
            behavior: typeof(IncomingLoggingMessageBehavior),
            description: "Logs incoming messages"
        );
endpointConfiguration.Pipeline.Register(
            behavior: typeof(OutgoingLoggingMessageBehavior),
            description: "Logs outgoing messages"
        );


var host = Host.CreateDefaultBuilder(args)
    .UseConsoleLifetime()
    .AddAggregatesNet(c => c
        .EventStore(es => es.AddClient("esdb://admin:changeit@localhost:2113?tls=false", "Domain"))
        .Domain()
        .NewtonsoftJson()
        .NServiceBus(endpointConfiguration)
        .SetCommandDestination("Domain")
        .SetTrackChildren())
    .ConfigureServices((context, services) =>
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConfiguration(context.Configuration.GetSection("Logging"));
            builder.AddFile(o => o.RootPath = AppContext.BaseDirectory);
        });
    }).Build();

await host.RunAsync();
