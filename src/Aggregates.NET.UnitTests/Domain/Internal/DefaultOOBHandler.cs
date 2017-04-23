using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Domain.Internal
{
    [TestFixture]
    public class DefaultOOBHandler
    {
        class Entity : Aggregates.Aggregate<Entity> { }
        

        private Moq.Mock<IStoreEvents> _store;
        private Aggregates.Internal.DefaultOobHandler _handler;
        private IEnumerable<IFullEvent> _events;

        [SetUp]
        public void Setup()
        {
            _store = new Moq.Mock<IStoreEvents>();
            _handler = new Aggregates.Internal.DefaultOobHandler(_store.Object, (a, b, c, d, e) => "test");


            _events = new IFullEvent[]
            {
                new Moq.Mock<IFullEvent>().Object
            };
        }

        [Test]
        public async Task publish_stores_in_store()
        {
            _store.Setup(
                x =>
                    x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                        Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>())).Returns(Task.FromResult(0L));
            _store.Setup(
                    x =>
                        x.WriteMetadata(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<long?>(),
                            Moq.It.IsAny<TimeSpan?>(), Moq.It.IsAny<TimeSpan?>(), Moq.It.IsAny<bool?>(),
                            Moq.It.IsAny<Guid?>(), Moq.It.IsAny<bool>(), Moq.It.IsAny<IDictionary<string, string>>()))
                .Returns(Task.CompletedTask);


            await _handler.Publish<Entity>("test", "test", new Id[] {}, _events, new Dictionary<string, string>()).ConfigureAwait(false);

            _store.Verify(x =>
                x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                    Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Once);
            _store.Verify(
                x =>
                    x.WriteMetadata(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<long?>(),
                        Moq.It.IsAny<TimeSpan?>(), Moq.It.IsAny<TimeSpan?>(), Moq.It.IsAny<bool?>(),
                        Moq.It.IsAny<Guid?>(), Moq.It.IsAny<bool>(), Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);
        }

        [Test]
        public async Task retreive_from_store()
        {
            _store.Setup(x => x.GetEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(_events));

            var retreived = await _handler.Retrieve<Entity>("test", "test", new Id[] {}).ConfigureAwait(false);

            _store.Verify(x => x.GetEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()),
                Moq.Times.Once);
            
        }
        [Test]
        public async Task retreive_from_store_backwards()
        {
            _store.Setup(x => x.GetEventsBackwards(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(_events));

            var retreived = await _handler.Retrieve<Entity>("test", "test", new Id[] { }, ascending: false).ConfigureAwait(false);

            _store.Verify(x => x.GetEventsBackwards(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()),
                Moq.Times.Once);
            
        }
        [Test]
        public async Task retreive_skip_take()
        {
            _store.Setup(x => x.GetEvents(Moq.It.IsAny<string>(), 1, 1))
                .Returns(Task.FromResult(_events));

            var retreived = await _handler.Retrieve<Entity>("test", "test", new Id[] { }, skip:1, take:1).ConfigureAwait(false);

            _store.Verify(x => x.GetEvents(Moq.It.IsAny<string>(), 1, 1),
                Moq.Times.Once);
        }

        [Test]
        public async Task size_from_store()
        {
            _store.Setup(x => x.Size(Moq.It.IsAny<string>())).Returns(Task.FromResult(1L));

            var size = await _handler.Size<Entity>("test", "test", new Id[] {}).ConfigureAwait(false);

            _store.Verify(x => x.Size(Moq.It.IsAny<string>()), Moq.Times.Once);
        }
    }
}
