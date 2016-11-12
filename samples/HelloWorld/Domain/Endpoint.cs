using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text;
using StructureMap;
using System.Threading;
using System.Threading.Tasks;
using Aggregates;
using Aggregates.Contracts;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Embedded;
using EventStore.ClientAPI.SystemData;
using NLog;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Pipeline;
using RabbitMQ.Client;

namespace Domain
{
    internal class Program
    {
        static readonly ManualResetEvent QuitEvent = new ManualResetEvent(false);
        private static IContainer _container;
        private static readonly NLog.ILogger Logger = LogManager.GetLogger("Domain");

        private static bool _embedded;

        private static void UnhandledExceptionTrapper(object sender, UnhandledExceptionEventArgs e)
        {
            Logger.Fatal(e.ExceptionObject);
            Console.WriteLine(e.ExceptionObject.ToString());
            Environment.Exit(1);
        }
        private static void ExceptionTrapper(object sender, FirstChanceExceptionEventArgs e)
        {
            //Logger.Debug(e.Exception, "Thrown exception: {0}");
        }

        private static void Main(string[] args)
        {
            ServicePointManager.UseNagleAlgorithm = false;
            var conf = NLog.Config.ConfigurationItemFactory.Default;
            NLog.LogManager.Configuration = new NLog.Config.XmlLoggingConfiguration($"{AppDomain.CurrentDomain.BaseDirectory}/logging.config");

            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionTrapper;
            AppDomain.CurrentDomain.FirstChanceException += ExceptionTrapper;

            NServiceBus.Logging.LogManager.Use<NLogFactory>();
            //EventStore.Common.Log.LogManager.SetLogFactory((name) => new EmbeddedLogger(name));

            var embedded = args.FirstOrDefault(x => x.StartsWith("--embedded"));
            try
            {
                _embedded = bool.Parse(embedded.Substring(embedded.IndexOf('=') + 1));
            }
            catch { }

            var client = ConfigureStore();
            var rabbit = ConfigureRabbit();

            _container = new Container(x =>
            {
                x.For<IEventStoreConnection>().Use(client).Singleton();
                x.For<IConnection>().Use(rabbit).Singleton();

                x.Scan(y =>
                {
                    y.TheCallingAssembly();
                    y.AssembliesFromApplicationBaseDirectory((assembly) => assembly.FullName.StartsWith("Domain"));

                    y.WithDefaultConventions();
                    y.AddAllTypesOf<ICommandMutator>();
                    y.AddAllTypesOf<IEventMutator>();
                });
            });

            var bus = InitBus().Result;
            _container.Configure(x => x.For<IMessageSession>().Use(bus).Singleton());

            Console.WriteLine("Press CTRL+C to exit...");
            Console.CancelKeyPress += (sender, eArgs) =>
            {
                QuitEvent.Set();
                eArgs.Cancel = true;
            };
            QuitEvent.WaitOne();

            bus.Stop().Wait();
        }
        private static async Task<IEndpointInstance> InitBus()
        {
            NServiceBus.Logging.LogManager.Use<NLogFactory>();

            var endpoint = "domain";

            var config = new EndpointConfiguration(endpoint);
            config.MakeInstanceUniquelyAddressable(Guid.NewGuid().ToString("N"));

            Logger.Info("Initializing Service Bus");

            config.EnableInstallers();
            config.LimitMessageProcessingConcurrencyTo(10);
            config.UseTransport<RabbitMQTransport>()
                //.CallbackReceiverMaxConcurrency(4)
                //.UseDirectRoutingTopology()
                .ConnectionStringName("RabbitMq")
                .PrefetchMultiplier(5)
                .TimeToWaitBeforeTriggeringCircuitBreaker(TimeSpan.FromSeconds(30));

            config.UseSerialization<NewtonsoftSerializer>();

            config.UsePersistence<InMemoryPersistence>();
            config.UseContainer<StructureMapBuilder>(c => c.ExistingContainer(_container));
            
            if (Logger.IsDebugEnabled)
            {
                config.EnableSlowAlerts(true);
                ////config.EnableCriticalTimePerformanceCounter();
                config.Pipeline.Register(
                    behavior: typeof(LogIncomingMessageBehavior),
                    description: "Logs incoming messages"
                    );
            }
            
            config.MaxConflictResolves(2);
            config.EnableFeature<Aggregates.Feature>();
            config.EnableFeature<Aggregates.Domain>();
            config.EnableFeature<Aggregates.GetEventStore>();
            config.Recoverability().ConfigureForAggregates(5, 3);
            //config.EnableFeature<RoutedFeature>();
            config.DisableFeature<Sagas>();


            return await Aggregates.Bus.Start(config).ConfigureAwait(false);
        }

        public static IConnection ConfigureRabbit()
        {
            var connectionString = ConfigurationManager.ConnectionStrings["RabbitMq"];
            if (connectionString == null)
                throw new ArgumentException("No Rabbit connection string");

            var data = connectionString.ConnectionString.Split(';');
            var host = data.FirstOrDefault(x => x.StartsWith("host", StringComparison.CurrentCultureIgnoreCase));
            if (host == null)
                throw new ArgumentException("No HOST parameter in rabbit connection string");
            var virtualhost = data.FirstOrDefault(x => x.StartsWith("virtualhost=", StringComparison.CurrentCultureIgnoreCase));

            var username = data.FirstOrDefault(x => x.StartsWith("username=", StringComparison.CurrentCultureIgnoreCase));
            var password = data.FirstOrDefault(x => x.StartsWith("password=", StringComparison.CurrentCultureIgnoreCase));

            host = host.Substring(5);
            virtualhost = virtualhost?.Substring(12) ?? "/";
            username = username?.Substring(9) ?? "guest";
            password = password?.Substring(9) ?? "guest";

            var factory = new ConnectionFactory { Uri = $"amqp://{username}:{password}@{host}:5672/{virtualhost}" };

            return factory.CreateConnection();
        }

        public static IEventStoreConnection ConfigureStore()
        {
            var connectionString = ConfigurationManager.ConnectionStrings["EventStore"];
            var data = connectionString.ConnectionString.Split(';');

            var hosts = data.Where(x => x.StartsWith("Host", StringComparison.CurrentCultureIgnoreCase));
            if (!hosts.Any())
                throw new ArgumentException("No Host parameter in eventstore connection string");
            var disk = data.FirstOrDefault(x => x.StartsWith("Disk=", StringComparison.CurrentCultureIgnoreCase));
            var embedded = data.FirstOrDefault(x => x.StartsWith("Embedded=", StringComparison.CurrentCultureIgnoreCase));
            var path = data.FirstOrDefault(x => x.StartsWith("Path=", StringComparison.CurrentCultureIgnoreCase));
            
            disk = disk?.Substring(5) ?? "false";
            embedded = embedded?.Substring(9) ?? "false";
            path = path?.Substring(5);

            var endpoints = hosts.Select(x =>
            {
                var addr = x.Substring(5).Split(':');
                if (addr[0] == "localhost")
                    return new IPEndPoint(IPAddress.Loopback, int.Parse(addr[1]));
                return new IPEndPoint(IPAddress.Parse(addr[0]), int.Parse(addr[1]));
            }).ToArray();

            Int32 tcpExtPort = endpoints[0].Port;

            var ip = IPAddress.Loopback;
            if (embedded == "true" || _embedded)
            {

                Logger.Info("Starting event store on {0} port {1}", ip, tcpExtPort);

                var builder = EmbeddedVNodeBuilder
                    .AsSingleNode()
                    .RunProjections(EventStore.Common.Options.ProjectionType.All)
                    .WithExternalHeartbeatInterval(TimeSpan.FromSeconds(30))
                    .WithInternalHeartbeatInterval(TimeSpan.FromSeconds(30))
                    .WithExternalHeartbeatTimeout(TimeSpan.FromMinutes(5))
                    .WithInternalHeartbeatTimeout(TimeSpan.FromMinutes(5))
                    .WithInternalTcpOn(new IPEndPoint(ip, tcpExtPort + 1))
                    .WithExternalTcpOn(new IPEndPoint(ip, tcpExtPort))
                    .WithInternalHttpOn(new IPEndPoint(ip, tcpExtPort - 2))
                    .WithExternalHttpOn(new IPEndPoint(ip, tcpExtPort - 1))
                    .AddInternalHttpPrefix($"http://*:{tcpExtPort - 2}/")
                    .AddExternalHttpPrefix($"http://*:{tcpExtPort - 1}/");


                if (disk == "true")
                {
                    System.IO.Directory.CreateDirectory(path);
                    builder = builder.RunOnDisk(path);
                }
                else
                {
                    builder = builder.RunInMemory();
                }
                var embeddedBuilder = builder.Build();

                var tcs = new TaskCompletionSource<Boolean>();

                embeddedBuilder.NodeStatusChanged += (_, e) =>
                {
                    Logger.Info("EventStore status changed: {0}", e.NewVNodeState);
                    if (!tcs.Task.IsCompleted && (e.NewVNodeState == EventStore.Core.Data.VNodeState.Master ||
                                                    e.NewVNodeState == EventStore.Core.Data.VNodeState.Slave ||
                                                    e.NewVNodeState == EventStore.Core.Data.VNodeState.Manager))
                    {
                        tcs.SetResult(true);
                    }
                };
                embeddedBuilder.Start();

                tcs.Task.Wait();

            }


            var cred = new UserCredentials("admin", "changeit");
            var settings = EventStore.ClientAPI.ConnectionSettings.Create()
                .KeepReconnecting()
                .KeepRetrying()
                .SetGossipSeedEndPoints(endpoints)
                .SetClusterGossipPort(endpoints.First().Port - 1)
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
                    .SetGossipSeedEndPoints(endpoints.Select(x => new IPEndPoint(x.Address, x.Port - 1)).ToArray())
                    .SetGossipTimeout(TimeSpan.FromMinutes(5))
                    .Build();

                client = EventStoreConnection.Create(settings, clusterSettings, "Domain");
            }
            else
                client = EventStoreConnection.Create(settings, endpoints.First(), "Domain");


            client.ConnectAsync().Wait();

            return client;
        }

    }

    public class LogIncomingMessageBehavior : Behavior<IIncomingLogicalMessageContext>
    {
        public override Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {

                Log.Debug("Received message '{0}'.\n" +
                                "ToString() of the message yields: {1}\n" +
                                "Message headers:\n{2}",
                                context.Message.MessageType != null ? context.Message.MessageType.AssemblyQualifiedName : "unknown",
                    context.Message.Instance,
                    string.Join(", ", context.MessageHeaders.Select(h => h.Key + ":" + h.Value).ToArray()));
            

            return next();

        }

        static readonly NLog.ILogger Log = LogManager.GetCurrentClassLogger();
    }
}
