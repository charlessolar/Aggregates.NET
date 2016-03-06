using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Metrics;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace Aggregates
{
    /// <summary>
    /// Keeps track of the domain events it handles and imposes a limit on the amount of events to process (allowing other instances to process others)
    /// Used for load balancing
    /// </summary>
    public class CompetingSubscriber : IEventSubscriber, IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(CompetingSubscriber));
        private readonly IEventStoreConnection _client;
        private readonly IPersistCheckpoints _checkpoints;
        private readonly IManageCompetes _competes;
        private readonly IDispatcher _dispatcher;
        private readonly ReadOnlySettings _settings;
        private readonly JsonSerializerSettings _jsonSettings;
        private readonly HashSet<String> _domains;
        private readonly Dictionary<String, long> _seenDomains;
        private readonly System.Threading.Timer _domainChecker;
        private Boolean _adopting;

        public CompetingSubscriber(IEventStoreConnection client, IPersistCheckpoints checkpoints, IManageCompetes competes, IDispatcher dispatcher, ReadOnlySettings settings, JsonSerializerSettings jsonSettings)
        {
            _client = client;
            _checkpoints = checkpoints;
            _competes = competes;
            _dispatcher = dispatcher;
            _settings = settings;
            _jsonSettings = jsonSettings;
            _domains = new HashSet<String>();
            _seenDomains = new Dictionary<String, long>();
            _adopting = false;

            var period = TimeSpan.FromSeconds(_settings.Get<Int32>("DomainHeartbeats"));
            _domainChecker = new System.Threading.Timer((state) =>
            {

                var maxDomains = _settings.Get<Int32>("HandledDomains");
                var expiration = TimeSpan.FromSeconds(_settings.Get<Int32>("DomainExpiration"));

                var consumer = (CompetingSubscriber)state;
                var endpoint = consumer._settings.EndpointName();
                
                var seenDomains = new Dictionary<String, long>(consumer._seenDomains);
                consumer._seenDomains.Clear();

                foreach (var seen in seenDomains)
                {
                    if (consumer._domains.Contains(seen.Key))
                        consumer._competes.Heartbeat(endpoint, seen.Key, DateTime.UtcNow, seen.Value);

                    var lastBeat = consumer._competes.LastHeartbeat(endpoint, seen.Key);
                    if ((DateTime.UtcNow - lastBeat) > expiration)
                    {
                        // We saw new events but the consumer for this domain has died, so we will adopt its domain
                        AdoptDomain(consumer, endpoint, seen.Key);
                        break;
                    }
                }



                var expiredDomains = new List<String>();
                // Check that each domain we are processing is still alive
                foreach (var domain in consumer._domains)
                {
                    var lastBeat = consumer._competes.LastHeartbeat(endpoint, domain);
                    if ((DateTime.UtcNow - lastBeat) > expiration)
                        expiredDomains.Add(domain);
                }
                expiredDomains.ForEach(x =>
                {
                    consumer._domains.Remove(x);
                });

                // If consumer has a slot available
                if (!consumer._adopting && consumer._domains.Count < maxDomains) {
                    // Make sure all active domains are being processed
                    
                }
            }, this, period, period);
        }
        public void Dispose()
        {
            this._domainChecker.Dispose();
        }

        private static void AdoptDomain(CompetingSubscriber consumer, String endpoint, String domain)
        {
            Logger.InfoFormat("Discovered orphaned domain {0}.. adopting", domain);
            consumer._adopting = true;
            var lastPosition = consumer._competes.LastPosition(endpoint, domain);
            consumer._client.SubscribeToAllFrom(new Position(lastPosition,lastPosition), false, (subscription, e) =>
            {
                Thread.CurrentThread.Rename("Eventstore");
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;

                var descriptor = e.Event.Metadata.Deserialize(consumer._jsonSettings);
                String headerDomain;
                if (!descriptor.Headers.TryGetValue(Aggregates.Defaults.DomainHeader, out headerDomain))
                    return;

                // Don't care about events that we are not adopting
                if (headerDomain != domain) return;
                
                var data = e.Event.Data.Deserialize(e.Event.EventType, consumer._jsonSettings);
                if (data == null) return;
                
                consumer._dispatcher.Dispatch(data, descriptor);

            }, liveProcessingStarted: (sub) =>
            {
                Logger.InfoFormat("Successfully adopted domain {0}", domain);
                consumer._domains.Add(domain);
                consumer._adopting = false;
                sub.Stop();
            }, subscriptionDropped: (_, reason, e) =>
            {
                Logger.WarnFormat("While adopting domain {0} the subscription dropped for reason: {1}.  Exception: {2}", domain, reason, e);
            });
        }

        public void SubscribeToAll(String endpoint)
        {
            var saved = _checkpoints.Load(endpoint);
            // To support HA simply save IManageCompetes data to a different db, in this way we can make clusters of consumers
            var maxDomains = _settings.Get<Int32>("HandledDomains");
            var heartbeats = TimeSpan.FromSeconds(_settings.Get<Int32>("DomainHeartbeats"));

            Logger.InfoFormat("Endpoint '{0}' subscribing to all events from position '{1}'", endpoint, saved);
            _client.SubscribeToAllFrom(saved, false, (subscription, e) =>
            {
                Thread.CurrentThread.Rename("Eventstore");
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;

                var descriptor = e.Event.Metadata.Deserialize(_jsonSettings);

                // If the event doesn't contain a domain header it was not generated by the domain
                String domain;
                if (!descriptor.Headers.TryGetValue(Aggregates.Defaults.DomainHeader, out domain))
                    return;

                if (e.OriginalPosition.HasValue)
                {
                    _seenDomains[domain] = e.OriginalPosition.Value.CommitPosition;

                    if (!_domains.Contains(domain))
                    {
                        if (_domains.Count >= maxDomains)
                            return;
                        else
                        {
                            // Returns true if it claimed the domain
                            if (_competes.CheckOrSave(endpoint, domain, e.OriginalPosition.Value.CommitPosition))
                                _domains.Add(domain);
                            else
                                return;
                        }
                    }
                }

                var data = e.Event.Data.Deserialize(e.Event.EventType, _jsonSettings);

                // Data is null for certain irrelevant eventstore messages (and we don't need to store position or snapshots)
                if (data == null) return;
                
                _dispatcher.Dispatch(data, descriptor);

            }, liveProcessingStarted: (_) =>
            {
                Logger.Info("Live processing started");
            }, subscriptionDropped: (_, reason, e) =>
            {
                Logger.WarnFormat("Subscription dropped for reason: {0}.  Exception: {1}", reason, e);
            });
        }

    }
}
