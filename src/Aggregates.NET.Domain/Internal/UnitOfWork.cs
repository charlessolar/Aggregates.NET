using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Internal;
using Metrics;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.ObjectBuilder.Common;
using NServiceBus.Pipeline.Contexts;
using NServiceBus.Unicast.Messages;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Aggregates.Internal
{
    public class UnitOfWork : IUnitOfWork
    {
        public static String PrefixHeader = "Originating";
        public static String MessageIdHeader = "Originating.NServiceBus.MessageId";
        public static String CommitIdHeader = "CommitId";
        public static String DomainHeader = "Domain";
        public static String NotFound = "<NOT FOUND>";

        // Header information to take from incoming messages
        public static IList<String> CarryOverHeaders = new List<String> {
                                                                          "NServiceBus.MessageId",
                                                                          "NServiceBus.CorrelationId",
                                                                          "NServiceBus.Version",
                                                                          "NServiceBus.TimeSent",
                                                                          "NServiceBus.ConversationId",
                                                                          "CorrId",
                                                                          "NServiceBus.OriginatingMachine",
                                                                          "NServiceBus.OriginatingEndpoint"
                                                                      };

        private static readonly ILog Logger = LogManager.GetLogger(typeof(UnitOfWork));
        private readonly IRepositoryFactory _repoFactory;

        private bool _disposed;
        private IDictionary<String, Object> _workHeaders;
        private IDictionary<Type, IRepository> _repositories;

        private Meter _commandsMeter = Metric.Meter("Commands", Unit.Commands);
        private Timer _commandsTimer = Metric.Timer("Commands Duration", Unit.Commands);
        private Counter _commandsConcurrent = Metric.Counter("Concurrent Commands", Unit.Commands);
        private TimerContext _timerContext;

        private Meter _errorsMeter = Metric.Meter("Command Errors", Unit.Errors);

        public IBuilder Builder { get; set; }

        public UnitOfWork(IRepositoryFactory repoFactory)
        {
            _repoFactory = repoFactory;
            _repositories = new Dictionary<Type, IRepository>();
            _workHeaders = new Dictionary<String, Object>();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public virtual void Dispose(bool disposing)
        {
            if (_disposed || !disposing)
                return;

            lock (_repositories)
            {
                foreach (var repo in _repositories)
                {
                    repo.Value.Dispose();
                }

                _repositories.Clear();
            }
            _disposed = true;
        }

        public IRepository<T> For<T>() where T : class, IAggregate
        {
            Logger.DebugFormat("Retreiving repository for type {0}", typeof(T));
            var type = typeof(T);

            IRepository repository;
            if (!_repositories.TryGetValue(type, out repository))
                _repositories[type] = repository = (IRepository)_repoFactory.ForAggregate<T>(Builder);

            return (IRepository<T>)repository;
        }
        public IEnumerable<TResponse> Query<TQuery, TResponse>(TQuery query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>
        {
            var processor = Builder.Build<IProcessor>();
            return processor.Process<TQuery, TResponse>(Builder, query);
        }
        public IEnumerable<TResponse> Query<TQuery, TResponse>(Action<TQuery> query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>
        {
            var result = (TQuery)FormatterServices.GetUninitializedObject(typeof(TQuery));
            query.Invoke(result);
            return Query<TQuery, TResponse>(result);
        }
        public TResponse Compute<TComputed, TResponse>(TComputed computed) where TComputed : IComputed<TResponse>
        {
            var processor = Builder.Build<IProcessor>();
            return processor.Compute<TComputed, TResponse>(Builder, computed);
        }
        public TResponse Compute<TComputed, TResponse>(Action<TComputed> computed) where TComputed : IComputed<TResponse>
        {
            var result = (TComputed)FormatterServices.GetUninitializedObject(typeof(TComputed));
            computed.Invoke(result);
            return Compute<TComputed, TResponse>(result);
        }

        public void Begin()
        {
            _commandsMeter.Mark();
            _commandsConcurrent.Increment();
            _timerContext = _commandsTimer.NewContext();
        }

        public void End(Exception ex)
        {
            _commandsConcurrent.Decrement();
            if (ex == null)
                Commit();
            else
                _errorsMeter.Mark();

            _timerContext.Dispose();
        }

        public void Commit()
        {
            var commitId = Guid.NewGuid();
            Object messageId;

            // Attempt to get MessageId from NServicebus headers
            // If we maintain a good CommitId convention it should solve the message idempotentcy issue (assuming the storage they choose supports it)
            if (_workHeaders.TryGetValue(MessageIdHeader, out messageId) && messageId is Guid)
                commitId = (Guid)messageId;

            // Allow the user to send a CommitId along with his message if he wants
            if (_workHeaders.TryGetValue(CommitIdHeader, out messageId) && messageId is Guid)
                commitId = (Guid)messageId;

            foreach (var repo in _repositories)
            {
                try
                {
                    // Insert all command headers into the commit
                    var headers = new Dictionary<String, Object>(_workHeaders);

                    repo.Value.Commit(commitId, headers);
                }
                catch (StorageException e)
                {
                    throw new PersistenceException(e.Message, e);
                }
            }
        }

        public void MutateOutgoing(LogicalMessage message, TransportMessage transportMessage)
        {
            // Insert our command headers into all messages sent by bus this unit of work
            foreach (var header in _workHeaders)
                transportMessage.Headers[header.Key] = header.Value.ToString();
        }

        public void MutateIncoming(TransportMessage transportMessage)
        {
            var headers = transportMessage.Headers;

            // There are certain headers that we can make note of
            // These will be committed to the event stream and included in all .Reply or .Publish done via this Unit Of Work
            // Meaning all receivers of events from the command will get information about the command's message, if they care
            foreach (var header in CarryOverHeaders)
            {
                var defaultHeader = "";
                headers.TryGetValue(header, out defaultHeader);

                if (String.IsNullOrEmpty(defaultHeader))
                    defaultHeader = NotFound;

                var workHeader = String.Format("{0}.{1}", PrefixHeader, header);
                _workHeaders[workHeader] = defaultHeader;
            }

            // Copy any application headers the user might have included
            var userHeaders = headers.Keys.Where(h =>
                            !h.Equals("CorrId", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals("WinIdName", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.StartsWith("NServiceBus", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.StartsWith("$", StringComparison.InvariantCultureIgnoreCase));

            foreach (var header in userHeaders)
                _workHeaders[header] = headers[header];

            _workHeaders[DomainHeader] = Domain.Current;
        }

        public Object MutateOutgoing(Object message)
        {
            return message;
        }
        public Object MutateIncoming(Object message)
        {
            this.CurrentMessage = message;
            return message;
        }

        public Object CurrentMessage { get; private set; }
    }
}