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
using NLog;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Pipeline;
using RabbitMQ.Client;
using Shared;


namespace Hello
{
    internal class Program
    {
        static readonly ManualResetEvent QuitEvent = new ManualResetEvent(false);
        private static IContainer _container;
        private static readonly NLog.ILogger Logger = LogManager.GetLogger("Hello");


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
            NLog.LogManager.Configuration =
                new NLog.Config.XmlLoggingConfiguration($"{AppDomain.CurrentDomain.BaseDirectory}/logging.config");

            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionTrapper;
            AppDomain.CurrentDomain.FirstChanceException += ExceptionTrapper;

            NServiceBus.Logging.LogManager.Use<NLogFactory>();
            //EventStore.Common.Log.LogManager.SetLogFactory((name) => new EmbeddedLogger(name));

            // Give event store time to start
            Thread.Sleep(TimeSpan.FromSeconds(10));

            var rabbit = ConfigureRabbit();

            _container = new Container(x =>
            {
                x.For<IConnection>().Use(rabbit).Singleton();

                x.Scan(y =>
                {
                    y.TheCallingAssembly();
                    y.AssembliesFromApplicationBaseDirectory((assembly) => assembly.FullName.StartsWith("Hello"));

                    y.WithDefaultConventions();
                    y.AddAllTypesOf<ICommandMutator>();
                    y.AddAllTypesOf<IEventMutator>();
                });
            });

            var bus = InitBus().Result;
            _container.Configure(x => x.For<IMessageSession>().Use(bus).Singleton());

            var running = true;

            var user = "test";
            Console.WriteLine("Please enter a username:");
            user = Console.ReadLine();
            Console.WriteLine($"Hello user {user}!  Use 'exit' to stop");
            do
            {
                Console.WriteLine("Please enter a message to send:");
                var message = Console.ReadLine();
                if (message.ToUpper() == "EXIT")
                    running = false;
                else
                {
                    bus.Send(new SayHello {User = user, Message = message});
                }

            } while (running);

            bus.Stop().Wait();
        }

        private static async Task<IEndpointInstance> InitBus()
        {
            NServiceBus.Logging.LogManager.Use<NLogFactory>();

            var endpoint = "hello";

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

            config.Pipeline.Remove("LogErrorOnInvalidLicense");
            config.EnableFeature<Aggregates.Feature>();
            config.Recoverability().ConfigureForAggregates();
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
            var virtualhost =
                data.FirstOrDefault(x => x.StartsWith("virtualhost=", StringComparison.CurrentCultureIgnoreCase));

            var username = data.FirstOrDefault(x => x.StartsWith("username=", StringComparison.CurrentCultureIgnoreCase));
            var password = data.FirstOrDefault(x => x.StartsWith("password=", StringComparison.CurrentCultureIgnoreCase));

            host = host.Substring(5);
            virtualhost = virtualhost?.Substring(12) ?? "/";
            username = username?.Substring(9) ?? "guest";
            password = password?.Substring(9) ?? "guest";

            var factory = new ConnectionFactory {Uri = $"amqp://{username}:{password}@{host}:5672/{virtualhost}"};

            return factory.CreateConnection();
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
