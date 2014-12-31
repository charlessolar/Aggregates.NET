using Aggregates.Contracts;
using NEventStore;
using NServiceBus;
using NServiceBus.ObjectBuilder;
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
        void Handle(String @event) { }
    }
    public class AggregateStub2 : Aggregate<Guid>
    {
    }
    public class AggregateStub3 : Aggregate<Guid>
    {

        // All invalid handles
        public void Handle(String @event) { }
        private void Handle(String @event, Int32 @event2) { }
        private void Handle() { }
        private Boolean Handle(Int32 @event) { return false; }
    }

    [TestFixture]
    public class ResolveTests
    {
        private Moq.Mock<IBuilder> _builder;
        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _builder.Setup(x => x.Build<IEventRouter>()).Returns(() => new Moq.Mock<IEventRouter>().Object);
            _builder.Setup(x => x.Build<IMessageCreator>()).Returns(() => new Moq.Mock<IMessageCreator>().Object);
            _builder.Setup(x => x.Build<IEventStream>()).Returns(() => new Moq.Mock<IEventStream>().Object);
        }

        [Test]
        public void resolve_stub()
        {
            var stub = new AggregateStub();
            //var result = resolver.Resolve(stub);
            //Assert.AreEqual(result.Count, 1);
            //Assert.True(result.ContainsKey(typeof(String)));
        }

        [Test]
        public void resolve_no_handles()
        {
            var stub = new AggregateStub2();
            //var result = resolver.Resolve(stub);
            //Assert.AreEqual(result.Count, 0);
        }


        [Test]
        public void resolve_improper_handles()
        {
            var stub = new AggregateStub3();
            //var result = resolver.Resolve(stub);
            //Assert.AreEqual(result.Count, 0);
        }
    }
}
