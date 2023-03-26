using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class Settings : ISettings
    {
        public IConfiguration Configuration { get; internal set; }
        public Version EndpointVersion { get; private set; }
        public Version AggregatesVersion { get; private set; }

        // Log settings
        public TimeSpan? SlowAlertThreshold { get; internal set; }
        public bool ExtraStats { get; internal set; }

        // Data settings
        public StreamIdGenerator Generator { get; internal set; }
        public int ReadSize { get; internal set; }
        public Compression Compression { get; internal set; }

        // Messaging settings
        public string Endpoint { get; internal set; }
        public string UniqueAddress { get; internal set; }
        public int Retries { get; internal set; }

		public TimeSpan SagaTimeout { get; internal set; }
		public bool AllEvents { get; internal set; }
        public bool TrackChildren { get; internal set; }

        // Disable certain "production" features related to versioning 
        public bool DevelopmentMode { get; internal set; }

        public string CommandDestination { get; internal set; }

        public string MessageContentType { get; internal set; }

        internal static List<Func<IServiceCollection, ISettings, Task>> RegistrationTasks;
        // Tasks which start command bus
        internal static List<Func<IServiceProvider, ISettings, Task>> BusTasks;
        // Tasks for after bus is online
        internal static List<Func<IServiceProvider, ISettings, Task>> StartupTasks;
        internal static List<Func<IServiceProvider, ISettings, Task>> ShutdownTasks;

        public Settings()
        {
            EndpointVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
            AggregatesVersion = Assembly.GetExecutingAssembly()?.GetName().Version ?? new Version(0, 0, 0);

            RegistrationTasks = new List<Func<IServiceCollection, ISettings, Task>>();
            BusTasks = new List<Func<IServiceProvider, ISettings, Task>>();
            StartupTasks = new List<Func<IServiceProvider, ISettings, Task>>();
            ShutdownTasks = new List<Func<IServiceProvider, ISettings, Task>>();

            Endpoint = "demo";
            // Set sane defaults
            Generator = new StreamIdGenerator((type, streamType, bucket, stream, parents) => $"{streamType}-{bucket}-[{parents.BuildParentsString()}]-{type}-{stream}");
            ReadSize = 100;
            Retries = 5;
            Compression = Compression.None;
            UniqueAddress = Guid.NewGuid().ToString("N");
            MessageContentType = "";
            SagaTimeout = TimeSpan.FromMinutes(10);

            RegistrationTasks.Add((container, settings) =>
            {

                container.AddSingleton<IRandomProvider>(new RealRandomProvider());
                container.AddSingleton<ITimeProvider>(new RealTimeProvider());

                container.AddTransient<IProcessor, Processor>();
                container.AddSingleton<IVersionRegistrar, VersionRegistrar>();

                container.AddTransient<IRepositoryFactory, RepositoryFactory>();
                container.AddTransient<IStoreSnapshots, StoreSnapshots>();
                container.AddTransient<IStoreEntities, StoreEntities>();
                container.AddSingleton<IEventSubscriber, EventSubscriber>();

                container.AddSingleton<ITrackChildren, TrackChildren>();

                container.AddTransient<IStoreEvents>((_) => null);
                container.AddTransient<IEventStoreConsumer>((_) => null);

                container.AddSingleton<IMetrics, NullMetrics>();

                container.AddSingleton<StreamIdGenerator>(Generator);

                container.AddSingleton<Action<string, string, Error>>((error, stack, message) =>
                {
                    message.Message = error;
                    message.Trace = stack;
                });

                container.AddSingleton<Action<Accept>>((_) =>
                {
                });

                container.AddSingleton<Action<BusinessException, Reject>>((ex, message) =>
                {
                    //message.Exception = ex;
                    message.Message = ex.Message;
                });

                return Task.CompletedTask;
            });
            BusTasks.Add((container, settings) =>
            {
                return Task.CompletedTask;
            });
            StartupTasks.Add((container, settings) =>
            {
                // perform initial versioned registration
                var versionRegistrar = container.GetService<IVersionRegistrar>();
                var messaging = container.GetService<Contracts.IMessaging>();

                if (versionRegistrar != null && messaging != null) {
                    versionRegistrar.Load(messaging.GetMessageTypes());
                    versionRegistrar.Load(messaging.GetEntityTypes());
                    versionRegistrar.Load(messaging.GetStateTypes());
                }
				return Task.CompletedTask;
            });
        }
        public Settings SetEndpointName(string endpoint)
        {
            Endpoint = endpoint;
            return this;
        }
        public Settings SetSlowAlertThreshold(TimeSpan? threshold)
        {
            SlowAlertThreshold = threshold;
            return this;
        }
        public Settings SetExtraStats(bool extra)
        {
            ExtraStats = extra;
            return this;
        }
        public Settings SetStreamIdGenerator(StreamIdGenerator generator)
        {
            Generator = generator;
            return this;
        }
        public Settings SetReadSize(int readsize)
        {
            ReadSize = readsize;
            return this;
        }
        public Settings SetCompression(Compression compression)
        {
            Compression = compression;
            return this;
        }
        public Settings SetUniqueAddress(string address)
        {
            UniqueAddress = address;
            return this;
        }
        public Settings SetCommandDestination(string destination)
        {
            CommandDestination = destination;
            return this;
        }
        public Settings ReceiveAllEvents()
        {
            AllEvents = true;
            return this;
        }
        public Settings SetRetries(int retry)
        {
            Retries = retry;
            return this;
        }
        public Settings SetTrackChildren(bool track = true)
        {
            TrackChildren = track;
            return this;
        }
        public Settings SetDevelopmentMode(bool mode = true)
        {
            DevelopmentMode = mode;
            return this;
        }
        public Settings SetSagaTimeout(TimeSpan timeout) {
            SagaTimeout = timeout;
            return this;
        }


        public Settings AddMetrics<TImplementation>() where TImplementation : class, IMetrics
        {
            RegistrationTasks.Add((container, settings) =>
            {
                container.AddSingleton<IMetrics, TImplementation>();
                return Task.CompletedTask;
            });
            return this;
        }
        public Settings Application<TImplementation>() where TImplementation : class, Aggregates.UnitOfWork.IApplicationUnitOfWork
        {
            RegistrationTasks.Add((container, settings) =>
            {
                container.AddScoped<TImplementation>();
                container.AddScoped<Aggregates.UnitOfWork.IApplicationUnitOfWork, TImplementation>();
                container.AddScoped<Aggregates.UnitOfWork.IUnitOfWork>(factory => factory.GetRequiredService<Aggregates.UnitOfWork.IApplicationUnitOfWork>());
                return Task.CompletedTask;
            });
            return this;
        }
        public Settings Application<TService, TImplementation>() where TImplementation : class, TService where TService : class, Aggregates.UnitOfWork.IApplicationUnitOfWork
        {
            // Hook up DI so a call to get IUnitOfWork, IApplicationUnitOfWork, and TService will return the same object
            RegistrationTasks.Add((container, settings) =>
            {
                container.AddScoped<TImplementation>();
                container.AddScoped<TService, TImplementation>();
                container.AddScoped<Aggregates.UnitOfWork.IApplicationUnitOfWork>(factory => factory.GetRequiredService<TService>());
                container.AddScoped<Aggregates.UnitOfWork.IUnitOfWork>(factory => factory.GetRequiredService<Aggregates.UnitOfWork.IApplicationUnitOfWork>());
                return Task.CompletedTask;
            });
            return this;
        }
        public Settings Domain()
        {
            RegistrationTasks.Add((container, settings) =>
            {
                container.TryAdd(ServiceDescriptor.Scoped<Aggregates.UnitOfWork.IDomainUnitOfWork, Internal.UnitOfWork>());
                container.AddScoped<Aggregates.UnitOfWork.IUnitOfWork>(factory => factory.GetRequiredService<Aggregates.UnitOfWork.IDomainUnitOfWork>());

                // IMutate is a depend on IStoreEvents which is used by IUnitOfWork
                // This causes a dependancy loop!
                // Break the loop by using Func<IMutate> instead
                container.AddTransient<Func<IMutate>>(factory => () => factory.GetRequiredService<Aggregates.UnitOfWork.IDomainUnitOfWork>());
                return Task.CompletedTask;
            });
            return this;
        }
        public Settings AddMutator<TMutate>(Func<IServiceProvider, IMutate> factory) where TMutate : class, IMutate
        {
            RegistrationTasks.Add((container, settings) =>
            {
                container.AddTransient<Func<IMutate>>(provider => () => factory(provider));
                return Task.CompletedTask;
            });
            return this;
        }
        public Settings AddMutator(IMutate mutate)
        {
            RegistrationTasks.Add((container, settings) =>
            {
                container.AddTransient<Func<IMutate>>(_ => () => mutate);
                return Task.CompletedTask;
            });
            return this;
        }
    }
}
