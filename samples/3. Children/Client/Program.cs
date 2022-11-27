using Aggregates;
using Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NServiceBus;
using Shared;


var csc = new CancellationTokenSource();
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
            .SetCommandDestination("Domain")
            .SetDevelopmentMode())
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

AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
{
    var logging = host.Services.GetRequiredService<ILogger>();
    logging.LogCritical($"Unhandled exception: {eventArgs.ExceptionObject.ToString()}");

    if (eventArgs.IsTerminating)
    {
        csc.Cancel();
        host.StopAsync();
        host.Dispose();
    }
};

await Task.WhenAny(host.RunAsync(csc.Token), Task.Run(() => Loop(host, csc.Token)));


static async Task<bool> WaitForAppStartup(IHostApplicationLifetime lifetime, CancellationToken stoppingToken)
{
    var startedSource = new TaskCompletionSource();
    var cancelledSource = new TaskCompletionSource();

    using var reg1 = lifetime.ApplicationStarted.Register(() => startedSource.SetResult());
    using var reg2 = stoppingToken.Register(() => cancelledSource.SetResult());

    Task completedTask = await Task.WhenAny(
        startedSource.Task,
        cancelledSource.Task).ConfigureAwait(false);

    // If the completed tasks was the "app started" task, return true, otherwise false
    return completedTask == startedSource.Task;
}

static async Task Loop(IHost host, CancellationToken token)
{
    // wait for app to start NSB connected etc etc
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    if (!await WaitForAppStartup(lifetime, token).ConfigureAwait(false))
    {
        // if canceled
        return;
    }

    var senderService = host.Services.GetRequiredService<ClientService>();

    Console.SetCursorPosition(0, 15);

    Console.WriteLine("Name the parent entity: ");
    var parentName = Console.ReadLine() ?? "Parent";
    await senderService.NameParent(parentName);

    while (true)
    {
        token.ThrowIfCancellationRequested();
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