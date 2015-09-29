using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Newtonsoft.Json;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;

namespace Aggregates
{
    public class VolatileSubscriber : IEventSubscriber
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(VolatileSubscriber));
        private readonly IBuilder _builder;
        private readonly IEventStoreConnection _client;
        private readonly JsonSerializerSettings _settings;

        public VolatileSubscriber(IBuilder builder, IEventStoreConnection client, JsonSerializerSettings settings)
        {
            _builder = builder;
            _client = client;
            _settings = settings;
        }

        public void SubscribeToAll(String endpoint, IDispatcher dispatcher)
        {
            _client.SubscribeToAllFrom(Position.End, false, (_, e) =>
            {
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;

                var descriptor = e.Event.Metadata.Deserialize(_settings);
                var data = e.Event.Data.Deserialize(e.Event.EventType, _settings);

                // Data is null for certain irrelevant eventstore messages (and we don't need to store position)
                if (data == null) return;

                var uow = _builder.Build<IConsumeUnitOfWork>();
                
                try
                {
                    uow.Start();
                    dispatcher.Dispatch(data);
                    uow.End();
                }
                catch (Exception ex)
                {
                    Logger.ErrorFormat("Error processing events.  Exception: {0}", ex);
                    uow.End(ex);
                }

            }, liveProcessingStarted: (_) =>
            {
                Logger.Debug("Live processing started");
            }, subscriptionDropped: (_, reason, e) =>
            {
                Logger.DebugFormat("Subscription dropped for reason: {0}.  Exception: {1}", reason, e.Message);
            });
        }
    }
}