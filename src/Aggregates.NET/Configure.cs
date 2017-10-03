using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Internal;
using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class Configuration
    {
        public static Configure Settings { get; internal set; }

        public static async Task Build(Action<Configure> settings)
        {
            var config = new Configure();
            settings(config);

            if (config.Container == null)
                throw new ArgumentException("Must designate a container implementation");

            Settings = config;

            try
            {
                await config.RegistrationTasks.WhenAllAsync(x => x(config)).ConfigureAwait(false);
                await config.SetupTasks.WhenAllAsync(x => x(config)).ConfigureAwait(false);
            }
            catch
            {
                Settings = null;
                throw;
            }
        }
    }


    public class Configure
    {

        // Log settings
        public TimeSpan? SlowAlertThreshold { get; private set; }
        public bool ExtraStats { get; private set; }

        // Data settings
        public StreamIdGenerator Generator { get; private set; }
        public int ReadSize { get; private set; }
        public Compression Compression { get; private set; }

        // Messaging settings
        public string Endpoint { get; private set; }
        public string UniqueAddress { get; private set; }
        public int Retries { get; private set; }
        public int ParallelMessages { get; private set; }
        public int ParallelEvents { get; private set; }
        public int MaxConflictResolves { get; private set; }

        // Delayed cache settings
        public int FlushSize { get; private set; }
        public TimeSpan FlushInterval { get; private set; }
        public TimeSpan DelayedExpiration { get; private set; }
        public int MaxDelayed { get; private set; }

        internal List<Func<Configure, Task>> RegistrationTasks;
        internal List<Func<Configure, Task>> SetupTasks;
        internal List<Func<Configure, Task>> StartupTasks;
        internal List<Func<Configure, Task>> ShutdownTasks;
        internal IContainer Container;

        public static Configure Start()
        {
            return new Configure();
        }

        public Configure()
        {
            RegistrationTasks = new List<Func<Configure, Task>>();
            SetupTasks = new List<Func<Configure, Task>>();
            StartupTasks = new List<Func<Configure, Task>>();
            ShutdownTasks = new List<Func<Configure, Task>>();

            Endpoint = "demo";
            // Set sane defaults
            Generator = new StreamIdGenerator((type, streamType, bucket, stream, parents) => $"{streamType}-{bucket}-[{parents.BuildParentsString()}]-{type.FullName.Replace(".", "")}-{stream}");
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

            RegistrationTasks.Add((c) =>
            {
                var container = c.Container;
                

                container.Register<IRepositoryFactory, RepositoryFactory>();
                container.Register<IProcessor, Processor>();
                container.Register<IDelayedChannel, DelayedChannel>();
                container.Register<IDomainUnitOfWork, UnitOfWork>();
                container.Register<IStoreSnapshots>((factory) => new StoreSnapshots(factory.Resolve<IMetrics>(), factory.Resolve<IStoreEvents>(), factory.Resolve<ISnapshotReader>(), c.Generator));
                container.Register<IStorePocos>((factory) => new StorePocos(factory.Resolve<IStoreEvents>(), factory.Resolve<ICache>(), factory.Resolve<IMessageSerializer>(), true, c.Generator));
                container.Register<ISnapshotReader, SnapshotReader>();

                container.RegisterSingleton<ICache, IntelligentCache>();
                container.RegisterSingleton<IMetrics, NullMetrics>();
                container.RegisterSingleton<IDelayedCache>((factory) => new DelayedCache(factory.Resolve<IMetrics>(), factory.Resolve<IStoreEvents>(), c.FlushInterval, c.Endpoint, c.MaxDelayed, c.FlushSize, c.DelayedExpiration, c.Generator));

                container.RegisterSingleton<IEventSubscriber>((factory) => new EventSubscriber(factory.Resolve<IMetrics>(), factory.Resolve<IMessaging>(), factory.Resolve<IEventStoreConsumer>(), c.ParallelEvents), "eventsubscriber");
                container.RegisterSingleton<IEventSubscriber>((factory) => new DelayedSubscriber(factory.Resolve<IMetrics>(), factory.Resolve<IEventStoreConsumer>(), factory.Resolve<IMessageDispatcher>(), c.Retries), "delayedsubscriber");
                container.RegisterSingleton<IEventSubscriber>((factory) => (IEventSubscriber)factory.Resolve<ISnapshotReader>(), "snapshotreader");

                container.RegisterSingleton<Func<Exception, string, Error>>((factory) =>
                {
                    var eventFactory = factory.Resolve<IEventFactory>();
                    return (exception, message) =>
                    {
                        var sb = new StringBuilder();
                        if (!string.IsNullOrEmpty(message))
                        {
                            sb.AppendLine($"Error Message: {message}");
                        }
                        sb.AppendLine($"Exception type {exception.GetType()}");
                        sb.AppendLine($"Exception message: {exception.Message}");
                        sb.AppendLine($"Stack trace: {exception.StackTrace}");


                        if (exception.InnerException != null)
                        {
                            sb.AppendLine("---BEGIN Inner Exception--- ");
                            sb.AppendLine($"Exception type {exception.InnerException.GetType()}");
                            sb.AppendLine($"Exception message: {exception.InnerException.Message}");
                            sb.AppendLine($"Stack trace: {exception.InnerException.StackTrace}");
                            sb.AppendLine("---END Inner Exception---");

                        }
                        var aggregateException = exception as System.AggregateException;
                        if (aggregateException == null)
                            return eventFactory.Create<Error>(e => { e.Message = sb.ToString(); });

                        sb.AppendLine("---BEGIN Aggregate Exception---");
                        var aggException = aggregateException;
                        foreach (var inner in aggException.InnerExceptions)
                        {

                            sb.AppendLine("---BEGIN Inner Exception--- ");
                            sb.AppendLine($"Exception type {inner.GetType()}");
                            sb.AppendLine($"Exception message: {inner.Message}");
                            sb.AppendLine($"Stack trace: {inner.StackTrace}");
                            sb.AppendLine("---END Inner Exception---");
                        }

                        return eventFactory.Create<Error>(e =>
                        {
                            e.Message = $"{message} - {exception.GetType().Name}: {exception.Message}";
                            e.Trace = sb.ToString();
                        });
                    };
                });

                container.RegisterSingleton<Func<Accept>>((factory) =>
                {
                    var eventFactory = factory.Resolve<IEventFactory>();
                    return () => eventFactory.Create<Accept>(x => { });
                });

                container.RegisterSingleton<Func<string, Reject>>((factory) =>
                {
                    var eventFactory = factory.Resolve<IEventFactory>();
                    return message => { return eventFactory.Create<Reject>(e => { e.Message = message; }); };
                });
                container.RegisterSingleton<Func<BusinessException, Reject>>((factory) =>
                {
                    var eventFactory = factory.Resolve<IEventFactory>();
                    return exception =>
                    {
                        return eventFactory.Create<Reject>(e =>
                        {
                            e.Exception = exception;
                            e.Message = $"{exception.GetType().Name} - {exception.Message}";
                        });
                    };
                });

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

    }
}
