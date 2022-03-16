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
        services.AddTransient<ClientService>();
    }).Build();


await Task.WhenAny(host.RunAsync(), Task.Run(() => Loop(host)));


static async Task Loop(IHost host) {
    // wait for app to start NSB connected etc etc
    var lifetime = host.Services.GetRequiredService<IHostLifetime>();
    await lifetime.WaitForStartAsync(CancellationToken.None);
    
    var senderService = host.Services.GetRequiredService<ClientService>();

    Console.SetCursorPosition(0, 15);

    Console.WriteLine("Name the parent entity: ");
    var parentName = Console.ReadLine() ?? "Parent";
    await senderService.NameParent(parentName);

    while (true)
    {
        Console.SetCursorPosition(0, 17);
        Console.WriteLine("Name a child: ");

        Console.Write("                                                                                                                                              ");
        Console.SetCursorPosition(0, 18);
        var childName = Console.ReadLine() ?? "Child";

        try
        {
            await senderService.AddChild(childName);
        }
        catch (Exception ex)
        {
            Console.SetCursorPosition(0, 19);
            Console.WriteLine($"Invalid child: {ex.Message}");
            Thread.Sleep(10000);
            Console.SetCursorPosition(0, 19);
            Console.Write("                                                                                                                                             ");
        }
    } 
}