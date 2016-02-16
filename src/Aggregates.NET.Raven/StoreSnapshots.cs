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
using Raven.Abstractions.Data;
using Raven.Json.Linq;

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
                    var id = String.Format("Snapshots/{0}.{1}", bucket, stream);
                    session.Store(snapshot.Payload, id);
                    var metadata = session.Advanced.GetMetadataFor(snapshot.Payload);

                    metadata[Constants.RavenEntityName] = _store.Conventions.FindTypeTagName(snapshot.Payload.GetType());
                    metadata.Add("Bucket", snapshot.Bucket);
                    metadata.Add("Stream", snapshot.Stream);
                    metadata.Add("EntityType", snapshot.EntityType);
                    metadata.Add("Timestamp", snapshot.Timestamp.ToString("o"));
                    metadata.Add("Version", snapshot.Version);
                }
                session.SaveChanges();
            }

            //foreach (var snapshot in snapshots)
            //{
            //    var id = String.Format("Snapshots/{0}.{1}", bucket, stream);
            //    // Manually PUT the memento so RavenDB doesn't mess with the Id fields and be goofy
            //    _store.DatabaseCommands.Put(
            //        id,
            //        null,
            //        RavenJObject.FromObject(snapshot.Payload), new RavenJObject
            //        {
            //            { Constants.RavenClrType, snapshot.EntityType },
            //            { Constants.RavenEntityName, _store.Conventions.FindTypeTagName(snapshot.Payload.GetType()) },
            //            { "Bucket", snapshot.Bucket },
            //            { "Stream", snapshot.Stream },
            //            { "EntityType", snapshot.EntityType },
            //            { "Timestamp", snapshot.Timestamp.ToString("o") },
            //            { "Version", snapshot.Version }
            //        });
            //}

        }

        public ISnapshot GetSnapshot(String bucket, String stream)
        {
            Logger.DebugFormat("Getting snapshot for stream '{0}' in bucket '{1}'", stream, bucket);

            using (var session = _store.OpenSession())
            {
                var id = String.Format("Snapshots/{0}.{1}", bucket, stream);
                
                var memento = session.Load<Object>(id);

                if (memento == null) return null;

                var metadata = session.Advanced.GetMetadataFor(memento);

                return new Internal.Snapshot
                {
                    EntityType = metadata.Value<String>("EntityType"),
                    Bucket = metadata.Value<String>("Bucket"),
                    Stream = metadata.Value<String>("Stream"),
                    Timestamp = metadata.Value<DateTime>("Timestamp"),
                    Version = metadata.Value<Int32>("Version"),
                    Payload = memento
                };
            }
        }

        public IEnumerable<ISnapshot> Query<T, TId, TMemento>(String bucket, Expression<Func<TMemento, bool>> predicate) where T : class, IEntity where TMemento : class, IMemento<TId>
        {
            using (var session = _store.OpenSession())
            {
                return session.Query<TMemento>().Where(predicate).ToList().Select(memento =>
                {
                    var metadata = session.Advanced.GetMetadataFor(memento);

                    return new Internal.Snapshot
                    {
                        EntityType = metadata.Value<String>("EntityType"),
                        Bucket = metadata.Value<String>("Bucket"),
                        Stream = metadata.Value<String>("Stream"),
                        Timestamp = metadata.Value<DateTime>("Timestamp"),
                        Version = metadata.Value<Int32>("Version"),
                        Payload = memento
                    };
                });
            }
        }
    }
}
