using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Linq.Expressions;
using NServiceBus.Logging;
using Raven.Client;
using Newtonsoft.Json;

namespace Aggregates.NET.Raven
{
    public class StoreSnapshots : IStoreSnapshots
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(StoreSnapshots));
        private readonly IDocumentStore _store;
        private readonly JsonSerializerSettings _settings;

        public StoreSnapshots(IDocumentStore store, JsonSerializerSettings settings)
        {
            _store = store;
            _settings = settings;
        }

        public void WriteSnapshots(string bucket, string stream, IEnumerable<ISnapshot> snapshots)
        {
            Logger.DebugFormat("Writing {0} snapshots to stream id '{1}' in bucket '{2}'", snapshots.Count(), stream, bucket);

            using (var session = _store.OpenSession())
            {
                foreach (var snapshot in snapshots)
                {
                    session.Store(snapshot.Payload);
                    var metadata = session.Advanced.GetMetadataFor(snapshot.Payload);

                    metadata.Add("EntityType", snapshot.EntityType);
                    metadata.Add("Timestamp", snapshot.Timestamp);
                    metadata.Add("Version", snapshot.Version);
                }
                session.SaveChanges();
            }
        }

        public ISnapshot GetSnapshot<T>(string bucket, string stream) where T : class, IEntity
        {
            Logger.DebugFormat("Getting snapshot for stream '{0}' in bucket '{1}'", stream, bucket);

            using(var session = _store.OpenSession())
            {
                var id = String.Format("Snapshots/{0}.{1}", bucket, stream);
                var snapshot = session.Load<object>(id);

                if (snapshot == null) return null;

                var metadata = session.Advanced.GetMetadataFor(snapshot);
                return snapshot;
            }
        }

        public IEnumerable<ISnapshot> Query<T, TId, TSnapshot>(string bucket, Expression<Func<TSnapshot, bool>> predicate) where T : class, IEntity where TSnapshot : class, IMemento<TId>
        {
            throw new NotImplementedException();
        }
    }
}
