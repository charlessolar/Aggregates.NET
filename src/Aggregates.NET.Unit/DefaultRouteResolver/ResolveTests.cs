using Aggregates.Contracts;
using NEventStore;
using NServiceBus;
using NServiceBus.ObjectBuilder.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.DefaultRouteResolver
{
    public class AggregateStub : Aggregate<Guid>
    {
        public AggregateStub(IContainer container) : base(container) { }
        void Handle(String @event) { }
    }
    public class AggregateStub2 : Aggregate<Guid>
    {
        public AggregateStub2(IContainer container) : base(container) { }
    }
    public class AggregateStub3 : Aggregate<Guid>
    {
        public AggregateStub3(IContainer container) : base(container) { }

        // All invalid handles
        public void Handle(String @event) { }
        private void Handle(String @event, Int32 @event2) { }
        private void Handle() { }
        private Boolean Handle(Int32 @event) { return false; }
    }

    [TestFixture]
    public class ResolveTests
    {
        private Moq.Mock<IContainer> _container;
        [SetUp]
        public void Setup()
        {
            _container = new Moq.Mock<IContainer>();
            _container.Setup(x => x.Build(typeof(IEventRouter))).Returns(() => new Moq.Mock<IEventRouter>().Object);
            _container.Setup(x => x.Build(typeof(IMessageCreator))).Returns(() => new Moq.Mock<IMessageCreator>().Object);
            _container.Setup(x => x.Build(typeof(IEventStream))).Returns(() => new Moq.Mock<IEventStream>().Object);
        }

        [Test]
        public void resolve_stub()
        {
            var stub = new AggregateStub(_container.Object);
            var resolver = new Aggregates.Internal.DefaultRouteResolver();
            var result = resolver.Resolve(stub);
            Assert.AreEqual(result.Count, 1);
            Assert.True(result.ContainsKey(typeof(String)));
        }

        [Test]
        public void resolve_no_handles()
        {
            var stub = new AggregateStub2(_container.Object);
            var resolver = new Aggregates.Internal.DefaultRouteResolver();
            var result = resolver.Resolve(stub);
            Assert.AreEqual(result.Count, 0);
        }


        [Test]
        public void resolve_improper_handles()
        {
            var stub = new AggregateStub3(_container.Object);
            var resolver = new Aggregates.Internal.DefaultRouteResolver();
            var result = resolver.Resolve(stub);
            Assert.AreEqual(result.Count, 0);
        }
    }
}
