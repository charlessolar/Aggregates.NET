using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using NServiceBus;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Domain.Internal
{
    [TestFixture]
    public class Entity
    {
        class Test : IEvent { }

        class FakeEntity : Aggregates.Internal.Entity<FakeEntity>
        {
            public int Handles;
            public int Conflicts;

            public bool Discard;

            public FakeEntity(IEventStream stream, IBuilder builder, IMessageCreator creator,
                IRouteResolver resolver)
            {
                (this as INeedStream).Stream = stream;
                (this as INeedBuilder).Builder = builder;
                (this as INeedEventFactory).EventFactory = creator;
                (this as INeedRouteResolver).Resolver = resolver;
            }

            private void Handle(Test e)
            {
                Handles++;
            }
            private void Conflict(Test e)
            {
                Conflicts++;

                if (Discard)
                    throw new DiscardEventException();
            }

            public void ApplyEvent()
            {
                Apply<Test>(x => { });
            }

            public void RaiseEvent()
            {
                Raise<Test>(x => { });
            }
        }

        private Moq.Mock<IUnitOfWork> _uow;
        private Moq.Mock<IEventStream> _stream;
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IMessageCreator> _creator;
        private Moq.Mock<IMessageMapper> _mapper;
        private Aggregates.Internal.DefaultRouteResolver _resolver;
        private FakeEntity _entity;



        [SetUp]
        public void Setup()
        {
            _uow = new Moq.Mock<IUnitOfWork>();
            _stream = new Moq.Mock<IEventStream>();
            _builder = new Moq.Mock<IBuilder>();
            _creator = new Moq.Mock<IMessageCreator>();
            _mapper = new Moq.Mock<IMessageMapper>();

            _creator.Setup(x => x.CreateInstance<Test>(Moq.It.IsAny<Action<Test>>())).Returns(new Test());
            _resolver = new Aggregates.Internal.DefaultRouteResolver(_mapper.Object);

            _builder.Setup(x => x.Build<IUnitOfWork>()).Returns(_uow.Object);

            _entity = new FakeEntity(_stream.Object, _builder.Object, _creator.Object, _resolver);
        }


        [Test]
        public async Task events_get_event()
        {
            _stream.Setup(x => x.Events(Moq.It.IsAny<long>(), Moq.It.IsAny<int>()))
                .Returns(Task.FromResult(new IFullEvent[] {}.AsEnumerable()));

            await _entity.HistoricalEvents(0, 1).ConfigureAwait(false);

            _stream.Verify(x => x.Events(Moq.It.IsAny<long>(), Moq.It.IsAny<int>()), Moq.Times.Once);
        }
        [Test]
        public async Task events_get_oobevent()
        {
            _stream.Setup(x => x.OobEvents(Moq.It.IsAny<long>(), Moq.It.IsAny<int>()))
                .Returns(Task.FromResult(new IFullEvent[] { }.AsEnumerable()));

            await _entity.HistoricalOobEvents(0, 1).ConfigureAwait(false);

            _stream.Verify(x => x.OobEvents(Moq.It.IsAny<long>(), Moq.It.IsAny<int>()), Moq.Times.Once);
        }

        [Test]
        public void entity_hash_code()
        {
            _stream.Setup(x => x.StreamId).Returns("test");

            Assert.AreEqual(new Id("test").GetHashCode(), _entity.GetHashCode());
        }

        [Test]
        public void hydrate_events()
        {
            var events = new[]
            {
                new Test(),
                new Test()
            };

            (_entity as IEventSourced).Hydrate(events);

            Assert.AreEqual(2, _entity.Handles);
        }

        [Test]
        public void conflicting_event()
        {
            _stream.Setup(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()));

            (_entity as IEventSourced).Conflict(new Test());

            Assert.AreEqual(1, _entity.Conflicts);
            Assert.AreEqual(1, _entity.Handles);

            _stream.Verify(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Once);
        }

        [Test]
        public void throw_discard_event_exception()
        {
            _stream.Setup(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()));

            _entity.Discard = true;
            (_entity as IEventSourced).Conflict(new Test());

            Assert.AreEqual(1, _entity.Conflicts);
            Assert.AreEqual(0, _entity.Handles);

            _stream.Verify(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Never);
        }

        [Test]
        public void apply_event()
        {
            _stream.Setup(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()));

            _entity.ApplyEvent();

            _stream.Verify(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Once);
        }
        [Test]
        public void raise_event()
        {
            _stream.Setup(x => x.AddOutOfBand(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()));

            _entity.RaiseEvent();

            _stream.Verify(x => x.AddOutOfBand(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Once);
        }
    }
}
