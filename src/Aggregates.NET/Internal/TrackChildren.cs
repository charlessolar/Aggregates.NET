using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class TrackChildren : ITrackChildren
    {
        private readonly ILogger Logger;

        private string _endpoint;
        private Version _version;
        private readonly bool _enabled;

        private readonly IEventStoreConsumer _consumer;
        private readonly IVersionRegistrar _registrar;

        public TrackChildren(ILogger<TrackChildren> logger, IEventStoreConsumer consumer, IVersionRegistrar registrar, ISettings settings)
        {
            Logger = logger;
            _consumer = consumer;
            _registrar = registrar;

            _enabled = settings.TrackChildren;
        }

        public Task Setup(string endpoint, Version version)
        {
            if (!_enabled)
                return Task.CompletedTask;

            _endpoint = endpoint;
            // Changes which affect minor version require a new projection, ignore revision and build numbers
            _version = new Version(version.Major, version.Minor);

            return _consumer.SetupChildrenProjection(endpoint, _version);
        }
        public async Task<TEntity[]> GetChildren<TEntity, TParent>(IDomainUnitOfWork uow, TParent parent) where TEntity : IChildEntity<TParent> where TParent : IHaveEntities<TParent>
        {
            if (!_enabled)
                throw new InvalidOperationException("Can not get children, TrackChildren is not enabled in settings");
            if(string.IsNullOrEmpty(_endpoint) || _version == null)
                throw new InvalidOperationException("Can not get children, TrackChildren was not setup");

            var parentEntityType = _registrar.GetVersionedName(typeof(TParent));
            var childEntityType = _registrar.GetVersionedName(typeof(TEntity));

            var data = await _consumer.GetChildrenData<TParent>(_version, parent).ConfigureAwait(false);
            var desiredChildren = data?.Children?.Where(x => x.EntityType == childEntityType).ToArray();

            if (desiredChildren == null || !desiredChildren.Any())
                return new TEntity[] { };

            Logger.DebugEvent("Hydrating", "Hydrating {Count} {ChildType} children of parent [{EntityType}] stream id [{StreamId}]", desiredChildren.Length, childEntityType, parentEntityType, parent.Id);

            var entities = new List<TEntity>();
            foreach (var child in desiredChildren)
            {
                var childEntity = await uow.For<TEntity, TParent>(parent).Get(child.StreamId).ConfigureAwait(false);
                entities.Add(childEntity);
            }
            return entities.ToArray();
        }

    }
}
