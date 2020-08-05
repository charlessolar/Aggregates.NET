using Aggregates;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using EventStore.ClientAPI.SystemData;
using Language;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Pipeline;
using NServiceBus.Serilog;
using RabbitMQ.Client;
using Serilog;
using StructureMap;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Client
{
    class Endpoint
    {
        static readonly ManualResetEvent QuitEvent = new ManualResetEvent(false);
        private static StructureMap.IContainer _container;
        private static void UnhandledExceptionTrapper(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Fatal("<{EventId:l}> Unhandled exception {Exception}", "Unhandled", e.ExceptionObject);
            Console.WriteLine("");
            Console.WriteLine("FATAL ERROR - Press return to close...");
            Console.ReadLine();
            Environment.Exit(1);
        }

        private static void ExceptionTrapper(object sender, FirstChanceExceptionEventArgs e)
        {
            //Logger.Debug(e.Exception, $"Thrown exception: {e.Exception}");
        }

        private static void Main(string[] args)
        {
            Console.Title = "Client";
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Warning()
               .WriteTo.Console(outputTemplate: "[{Level}] {Message}{NewLine}{Exception}")
               .CreateLogger();


            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionTrapper;
            AppDomain.CurrentDomain.FirstChanceException += ExceptionTrapper;


            ServicePointManager.UseNagleAlgorithm = false;

            NServiceBus.Logging.LogManager.Use<SerilogFactory>();

            _container = new StructureMap.Container(x =>
            {
                x.Scan(y =>
                {
                    y.TheCallingAssembly();

                    y.WithDefaultConventions();
                });
            });

            var bus = InitBus().Result;

            var running = true;

            Console.WriteLine($"Use 'exit' to stop");
            Console.SetCursorPosition(Console.CursorLeft, Console.WindowTop + Console.WindowHeight - 2);
            Console.WriteLine("Please enter a message to send:");
            do
            {
                var message = Console.ReadLine();

                // clear input
                var current = Console.CursorTop - 1;
                Console.SetCursorPosition(0, Console.CursorTop - 1);
                Console.Write(new string(' ', Console.WindowWidth));
                Console.SetCursorPosition(0, current + 1);

                if (message.ToUpper() == "EXIT")
                    running = false;
                else
                {
                    try
                    {
                        bus.Command("domain", new SayHello { MessageId = Guid.NewGuid(), Message = message }).Wait();
                    }
                    catch (AggregateException e)
                    {
                        var rejection = e.InnerException;

                        Log.Warning("<{EventId:l}> Command rejected due to: {Message}", "Rejection", rejection.Message);
                    }
                }

            } while (running);

            bus.Stop().Wait();
        }

        private static async Task<IEndpointInstance> InitBus()
        {
            var endpoint = "client";

            var config = new EndpointConfiguration(endpoint);

            Log.Information("<{EventId:l}> Initializing Service Bus", "Init");


            config.UseTransport<LearningTransport>();
            config.SendFailedMessagesTo("error");

            config.UseSerialization<NewtonsoftSerializer>();

            config.UsePersistence<InMemoryPersistence>();
            

            if (Log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
            {
                ////config.EnableCriticalTimePerformanceCounter();
                config.Pipeline.Register(
                    behavior: typeof(LogIncomingMessageBehavior),
                    description: "Logs incoming messages"
                );
            }

            config.Pipeline.Remove("LogErrorOnInvalidLicense");
            //config.EnableFeature<RoutedFeature>();
            config.DisableFeature<Sagas>();


            var client = await ConfigureStore();

            await Aggregates.Configuration.Build(c => c
                    .StructureMap(_container)
                    .EventStore(new[] { client })
                    .NewtonsoftJson()
                    .NServiceBus(config)
                    );

            await Aggregates.Configuration.Start().ConfigureAwait(false);

            return Aggregates.Bus.Instance;
        }

        public static async Task<IEventStoreConnection> ConfigureStore()
        {
            var connectionString = "host=localhost:1113;";
            var data = connectionString.Split(';');

            var hosts = data.Where(x => x.StartsWith("Host", StringComparison.CurrentCultureIgnoreCase));
            if (!hosts.Any())
                throw new ArgumentException("No Host parameter in eventstore connection string");


            var endpoints = hosts.Select(x =>
            {
                var addr = x.Substring(5).Split(':');
                if (addr[0] == "localhost")
                    return new IPEndPoint(IPAddress.Loopback, int.Parse(addr[1]));
                return new IPEndPoint(IPAddress.Parse(addr[0]), int.Parse(addr[1]));
            }).ToArray();

            var cred = new UserCredentials("admin", "changeit");
            var settings = EventStore.ClientAPI.ConnectionSettings.Create()
                .KeepReconnecting()
                .KeepRetrying()
                .SetGossipSeedEndPoints(endpoints)
                .SetClusterGossipPort(2113)
                .SetHeartbeatInterval(TimeSpan.FromSeconds(30))
                .SetGossipTimeout(TimeSpan.FromMinutes(5))
                .SetHeartbeatTimeout(TimeSpan.FromMinutes(5))
                .SetTimeoutCheckPeriodTo(TimeSpan.FromMinutes(1))
                .SetDefaultUserCredentials(cred);

            IEventStoreConnection client;
            if (hosts.Count() != 1)
            {
                var clusterSettings = EventStore.ClientAPI.ClusterSettings.Create()
                    .DiscoverClusterViaGossipSeeds()
                    .SetGossipSeedEndPoints(endpoints.Select(x => new IPEndPoint(x.Address, 2113)).ToArray())
                    .SetGossipTimeout(TimeSpan.FromMinutes(5))
                    .Build();

                client = EventStoreConnection.Create(settings, clusterSettings, "Events");
            }
            else
                client = EventStoreConnection.Create(settings, endpoints.First(), "Events");


            await client.ConnectAsync();
            
            return client;
        }

    }
    public class LogIncomingMessageBehavior : Behavior<IIncomingLogicalMessageContext>
    {
        public override Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {

            Log.Debug("<{EventId:l}> Received message '{MessageType}'.\n" +
                            "ToString() of the message yields: {MessageBody}\n" +
                            "Message headers:\n{MessageHeaders}", "Incoming",
                            context.Message.MessageType != null ? context.Message.MessageType.AssemblyQualifiedName : "unknown",
                context.Message.Instance,
                string.Join(", ", context.MessageHeaders.Select(h => h.Key + ":" + h.Value).ToArray()));


            return next();

        }

    }
}
