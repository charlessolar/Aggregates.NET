using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Internal;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Domain.Internal.ConflictResolvers
{
    [TestFixture]
    public class EasyConflictResolvers
    {
        class Entity : Aggregates.Aggregate<Entity>
        {
            public Entity(IEventStream stream, IRouteResolver resolver)
            {
                (this as INeedStream).Stream = stream;
                (this as INeedRouteResolver).Resolver = resolver;
            }
        }
        class Event : IEvent { }

        private Moq.Mock<IEventStream> _stream;
        private Moq.Mock<IRouteResolver> _resolver;

        [SetUp]
        public void Setup()
        {
            _stream = new Moq.Mock<IEventStream>();
            _resolver = new Moq.Mock<IRouteResolver>();
        }

        [Test]
        public void throw_resolver()
        {
            // Does not resolve, just throws
            var resolver = new ThrowConflictResolver();

            var fullevent = new Moq.Mock<IFullEvent>();
            fullevent.Setup(x => x.Event).Returns(new Event());

            var entity = new Entity(_stream.Object, _resolver.Object);
            Assert.ThrowsAsync<ConflictResolutionFailedException>(
                () => resolver.Resolve(entity, new[] { fullevent.Object }, Guid.NewGuid(), new Dictionary<string, string>()));

        }

        [Test]
        public async Task ignore_resolver()
        {
            var store = new Moq.Mock<IStoreEvents>();
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            var fullevent = new Moq.Mock<IFullEvent>();
            fullevent.Setup(x => x.Event).Returns(new Event());

            _stream.Setup(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()));
            store.Setup(
                    x => x.WriteEvents("test", new[] {fullevent.Object}, Moq.It.IsAny<IDictionary<string, string>>(), null))
                .Returns(Task.FromResult(0L));

            // Ignores conflict, just commits
            var resolver = new IgnoreConflictResolver(store.Object, streamGen);

            var entity = new Entity(_stream.Object, _resolver.Object);


            await resolver.Resolve(entity, new[] {fullevent.Object}, Guid.NewGuid(), new Dictionary<string, string>())
                .ConfigureAwait(false);

            _stream.Verify(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);
            store.Verify(
                x => x.WriteEvents("test", new[] {fullevent.Object}, Moq.It.IsAny<IDictionary<string, string>>(), null),
                Moq.Times.Once);
        }

        [Test]
        public async Task discard_resolver()
        {
            var store = new Moq.Mock<IStoreEvents>();

            var fullevent = new Moq.Mock<IFullEvent>();
            fullevent.Setup(x => x.Event).Returns(new Event());

            _stream.Setup(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()));
            store.Setup(
                    x => x.WriteEvents("test", new[] { fullevent.Object }, Moq.It.IsAny<IDictionary<string, string>>(), null))
                .Returns(Task.FromResult(0L));

            // Discards all conflicted events, doesn't save
            var resolver = new DiscardConflictResolver();

            var entity = new Entity(_stream.Object, _resolver.Object);

            await resolver.Resolve(entity, new[] { fullevent.Object }, Guid.NewGuid(), new Dictionary<string, string>())
                .ConfigureAwait(false);

            _stream.Verify(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Never);
            store.Verify(
                x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(), Moq.It.IsAny<IDictionary<string, string>>(), null),
                Moq.Times.Never);
        }


    }
}
