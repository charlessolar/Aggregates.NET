using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Messages;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.UnitTests.Common
{
    [TestFixture]
    public class State
    {
        class Test : IEvent { }

        class FakeState : Aggregates.State<FakeState>
        {
            public int Handles = 0;
            public int Conflicts = 0;
            public bool Discard = false;
            public bool SnapshotRestored = false;

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

            protected override void RestoreSnapshot(FakeState snapshot)
            {
                SnapshotRestored = snapshot.SnapshotRestored;
            }
        }

        private Moq.Mock<IEventMapper> _mapper;
        private FakeState _state;

        [SetUp]
        public void Setup()
        {
            _state = new FakeState();
            _mapper = new Moq.Mock<IEventMapper>();

            _mapper.Setup(x => x.GetMappedTypeFor(typeof(Test))).Returns(typeof(Test));

            var fake = new FakeConfiguration();
            fake.FakeContainer.Setup(x => x.Resolve<IEventMapper>()).Returns(_mapper.Object);
            Configuration.Build(fake).Wait();
        }

        [Test]
        public void apply_version_increment()
        {
            Assert.AreEqual(0, _state.Version);

            (_state as IState).Apply(new Test());
            Assert.AreEqual(1, _state.Handles);

            Assert.AreEqual(1, _state.Version);
        }

        [Test]
        public void conflict_version_same()
        {
            Assert.AreEqual(0, _state.Version);

            (_state as IState).Conflict(new Test());
            Assert.AreEqual(1, _state.Conflicts);

            Assert.AreEqual(0, _state.Version);
        }

        [Test]
        public void restore_snapshot()
        {
            var snapshot = new FakeState { SnapshotRestored=true };

            (_state as IState).RestoreSnapshot(snapshot);

            Assert.IsTrue(snapshot.SnapshotRestored);
        }

        
    }
}
