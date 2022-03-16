using Aggregates;
using Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NServiceBus;
using Shared;


var endpointConfiguration = new EndpointConfiguration("Client");
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
            .EventStore(es => es.AddClient("esdb://admin:changeit@localhost:2113?tls=false", "Client"))
            .NewtonsoftJson()
            .NServiceBus(endpointConfiguration)
            .SetCommandDestination("Domain"))
    .ConfigureServices((context, services) =>
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConfiguration(context.Configuration.GetSection("Logging"));
            builder.AddFile(o => o.RootPath = AppContext.BaseDirectory);
        });
        services.AddTransient<EchoService>();
    }).Build();


await Task.WhenAny(host.RunAsync(), Task.Run(() => Loop(host)));


static async Task Loop(IHost host)
{
    // wait for app to start NSB connected etc etc
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    lifetime.ApplicationStarted.WaitHandle.WaitOne();

    var senderService = host.Services.GetRequiredService<EchoService>();

    while (true)
    {
        Console.SetCursorPosition(0, 15);
        Console.WriteLine("Press any key to send hello");
        Console.ReadKey();
        try
        {
            await senderService.SendMessage("Hello World")
                .ConfigureAwait(false);
        }catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}
