using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Internal;
using Aggregates.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates
{
    public class Configuration : IConfiguration
    {



        public bool Setup => Settings != null;
        public Configure Settings { get; internal set; }

        public async Task Start()
        {
            if (Settings == null)
                throw new InvalidOperationException("Settings must be built");

            try
            {
                await Settings.SetupTasks.WhenAllAsync(x => x(Settings)).ConfigureAwait(false);
            }
            catch
            {
                Settings = null;
                throw;
            }
        }


        public async static Task<IConfiguration> Build(Action<Configure> settings)
        {
            var config = new Configure();
            settings(config);

            if (config.Container == null)
                throw new InvalidOperationException("Must designate a container implementation");

            var aggConfig = new Configuration();

            aggConfig.Settings = config;

            try
            {
                await config.RegistrationTasks.WhenAllAsync(x => x(config)).ConfigureAwait(false);
                config.Container.Register<IConfiguration>(aggConfig, Lifestyle.Singleton);
                config.Container.Register<Configure>((_) => aggConfig.Settings, Lifestyle.Singleton);
            }
            catch
            {
                throw;
            }
            return aggConfig;
        }
    }


    public class Configure
    {
        public readonly Version EndpointVersion;
        public readonly Version AggregatesVersion;

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
        public int ParallelMessages { get; internal set; }
        public int ParallelEvents { get; internal set; }
        public int MaxConflictResolves { get; internal set; }

        // Delayed cache settings
        public int FlushSize { get; internal set; }
        public TimeSpan FlushInterval { get; internal set; }
        public TimeSpan DelayedExpiration { get; internal set; }
        public int MaxDelayed { get; internal set; }

        public bool AllEvents { get; internal set; }
        public bool Passive { get; internal set; }
        public bool TrackChildren { get; internal set; }

        // Disable certain "production" features related to versioning 
        public bool DevelopmentMode { get; internal set; }

        public string CommandDestination { get; internal set; }

        public string MessageContentType { get; internal set; }

        internal List<Func<Configure, Task>> RegistrationTasks;
        internal List<Func<Configure, Task>> SetupTasks;
        internal List<Func<Configure, Task>> StartupTasks;
        internal List<Func<Configure, Task>> ShutdownTasks;

        internal AsyncLocal<IContainer> LocalContainer;
        internal IContainer Container;
        
        public Configure()
        {
            EndpointVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
            AggregatesVersion = Assembly.GetExecutingAssembly()?.GetName().Version ?? new Version(0, 0, 0);

            RegistrationTasks = new List<Func<Configure, Task>>();
            SetupTasks = new List<Func<Configure, Task>>();
            StartupTasks = new List<Func<Configure, Task>>();
            ShutdownTasks = new List<Func<Configure, Task>>();

            Endpoint = "demo";
            // Set sane defaults
            Generator = new StreamIdGenerator((type, streamType, bucket, stream, parents) => $"{streamType}-{bucket}-[{parents.BuildParentsString()}]-{type}-{stream}");
            ReadSize = 100;
            Compression = Compression.None;
            UniqueAddress = Guid.NewGuid().ToString("N");
            Retries = 10;
            ParallelMessages = 10;
            ParallelEvents = 10;
            MaxConflictResolves = 3;
            FlushSize = 500;
            FlushInterval = TimeSpan.FromMinutes(1);
            DelayedExpiration = TimeSpan.FromMinutes(5);
            MaxDelayed = 5000;
            MessageContentType = "";
            LocalContainer = new AsyncLocal<IContainer>();

            RegistrationTasks.Add((c) =>
            {
                var container = c.Container;

                container.Register<IRandomProvider>(new RealRandomProvider(), Lifestyle.Singleton);
                container.Register<ITimeProvider>(new RealTimeProvider(), Lifestyle.Singleton);

                // Provide a "default" logger so user doesnt need to provide if they dont want to
                container.Register<ILoggerFactory>(NullLoggerFactory.Instance, Lifestyle.PerInstance);

                // Register ourselves with ourselves
                container.Register<IContainer>(container, Lifestyle.Singleton);

                container.Register<IProcessor, Processor>(Lifestyle.PerInstance);
                container.Register<IVersionRegistrar, VersionRegistrar>(Lifestyle.Singleton);

                if (!c.Passive)
                {
                    // A library which managing UOW needs to register the domain unit of work. 
                    // DI containers are designed to append registrations if multiple are present
                    //container.Register<UnitOfWork.IDomain, Internal.UnitOfWork>(Lifestyle.UnitOfWork);

                    container.Register<IDelayedChannel, DelayedChannel>(Lifestyle.UnitOfWork);
                    container.Register<IRepositoryFactory, RepositoryFactory>(Lifestyle.PerInstance);
                    container.Register<IStoreSnapshots>((factory) => new StoreSnapshots(factory.Resolve<ILoggerFactory>(), this, factory.Resolve<IMetrics>(), factory.Resolve<IStoreEvents>(), factory.Resolve<ISnapshotReader>(), factory.Resolve<IVersionRegistrar>()), Lifestyle.Singleton);
                    container.Register<IOobWriter>((factory) => new OobWriter(factory.Resolve<ILoggerFactory>(), this, factory.Resolve<IMessageDispatcher>(), factory.Resolve<IStoreEvents>(), factory.Resolve<IVersionRegistrar>()), Lifestyle.Singleton);
                    container.Register<ISnapshotReader, SnapshotReader>(Lifestyle.Singleton);
                    container.Register<IStoreEntities, StoreEntities>(Lifestyle.Singleton);
                    container.Register<IDelayedCache>((factory) => new DelayedCache(factory.Resolve<ILoggerFactory>(), this, factory.Resolve<IMetrics>(), factory.Resolve<IStoreEvents>(), factory.Resolve<IVersionRegistrar>(), factory.Resolve<IRandomProvider>(), factory.Resolve<ITimeProvider>()), Lifestyle.Singleton);

                    container.Register<IEventSubscriber>((factory) => new EventSubscriber(factory.Resolve<ILoggerFactory>(), this, factory.Resolve<IMetrics>(), factory.Resolve<IMessaging>(), factory.Resolve<IEventStoreConsumer>(), factory.Resolve<IVersionRegistrar>(), c.ParallelEvents, c.AllEvents), Lifestyle.Singleton, "eventsubscriber");
                    container.Register<IEventSubscriber>((factory) => new DelayedSubscriber(factory.Resolve<ILoggerFactory>(), this, factory.Resolve<IMetrics>(), factory.Resolve<IEventStoreConsumer>(), factory.Resolve<IMessageDispatcher>(), c.Retries), Lifestyle.Singleton, "delayedsubscriber");
                    container.Register<IEventSubscriber>((factory) => (IEventSubscriber)factory.Resolve<ISnapshotReader>(), Lifestyle.Singleton, "snapshotreader");

                    container.Register<ITrackChildren, TrackChildren>(Lifestyle.Singleton);

                }
                container.Register<IMetrics, NullMetrics>(Lifestyle.Singleton);

                container.Register<StreamIdGenerator>(Generator, Lifestyle.Singleton);

                container.Register<Action<Exception, string, Error>>((ex, error, message) =>
                {
                    message.Message = $"{message} - {ex.GetType().Name}: {ex.Message}";
                    message.Trace = ex.AsString();
                }, Lifestyle.Singleton);

                container.Register<Action<Accept>>((_) =>
                {
                }, Lifestyle.Singleton);

                container.Register<Action<BusinessException, Reject>>((ex, message) =>
                {
                    message.Exception = ex;
                    message.Message= $"{ex.GetType().Name} - {ex.Message}";
                }, Lifestyle.Singleton);

                return Task.CompletedTask;
            });
            StartupTasks.Add((c) =>
            {
                return Task.CompletedTask;
            });
        }
        public Configure SetEndpointName(string endpoint)
        {
            Endpoint = endpoint;
            return this;
        }
        public Configure SetSlowAlertThreshold(TimeSpan? threshold)
        {
            SlowAlertThreshold = threshold;
            return this;
        }
        public Configure SetExtraStats(bool extra)
        {
            ExtraStats = extra;
            return this;
        }
        public Configure SetStreamIdGenerator(StreamIdGenerator generator)
        {
            Generator = generator;
            return this;
        }
        public Configure SetReadSize(int readsize)
        {
            ReadSize = readsize;
            return this;
        }
        public Configure SetCompression(Compression compression)
        {
            Compression = compression;
            return this;
        }
        public Configure SetUniqueAddress(string address)
        {
            UniqueAddress = address;
            return this;
        }
        public Configure SetRetries(int retries)
        {
            Retries = retries;
            return this;
        }
        public Configure SetParallelMessages(int parallel)
        {
            ParallelMessages = parallel;
            return this;
        }
        public Configure SetParallelEvents(int parallel)
        {
            ParallelEvents = parallel;
            return this;
        }
        public Configure SetMaxConflictResolves(int attempts)
        {
            MaxConflictResolves = attempts;
            return this;
        }
        public Configure SetFlushSize(int size)
        {
            FlushSize = size;
            return this;
        }
        public Configure SetFlushInterval(TimeSpan interval)
        {
            FlushInterval = interval;
            return this;
        }
        public Configure SetDelayedExpiration(TimeSpan expiration)
        {
            DelayedExpiration = expiration;
            return this;
        }
        public Configure SetMaxDelayed(int max)
        {
            MaxDelayed = max;
            return this;
        }
        public Configure SetCommandDestination(string destination)
        {
            CommandDestination = destination;
            return this;
        }
        /// <summary>
        /// Passive means the endpoint doesn't need a unit of work, it won't process events or commands
        /// </summary>
        /// <returns></returns>
        public Configure SetPassive()
        {
            Passive = true;
            return this;
        }
        public Configure ReceiveAllEvents()
        {
            AllEvents = true;
            return this;
        }
        public Configure SetTrackChildren(bool track = true)
        {
            TrackChildren = track;
            return this;
        }
        public Configure SetDevelopmentMode(bool mode = true)
        {
            DevelopmentMode = mode;
            return this;
        }


        public Configure AddMetrics<TImplementation>() where TImplementation : class, IMetrics
        {
            RegistrationTasks.Add((c) =>
            {
                c.Container.Register<IMetrics, TImplementation>(Lifestyle.Singleton);
                return Task.CompletedTask;
            });
            return this;
        }
        public Configure AddLogging(ILoggerFactory factory)
        {
            RegistrationTasks.Add((c) =>
            {
                c.Container.Register<ILoggerFactory>(factory, Lifestyle.Singleton);
                return Task.CompletedTask;
            });
            return this;
        }
    }
}
