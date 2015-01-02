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
        private Aggregates.Internal.DefaultRouteResolver _resolver;

        [SetUp]
        public void Setup()
        {
            _resolver = new Aggregates.Internal.DefaultRouteResolver();
        }

        [Test]
        public void resolve_stub()
        {
            var stub = new AggregateStub();

            var result = _resolver.Resolve(stub, typeof(String));
            Assert.NotNull(result);
        }

        [Test]
        public void resolve_no_handles()
        {
            var stub = new AggregateStub2();
            var result = _resolver.Resolve(stub, typeof(String));
            Assert.Null(result);
        }


        [Test]
        public void resolve_improper_handles()
        {
            var stub = new AggregateStub3();
            var result = _resolver.Resolve(stub, typeof(String));
            Assert.Null(result);
            result = _resolver.Resolve(stub, typeof(Int32));
            Assert.Null(result);
            result = _resolver.Resolve(stub, typeof(void));
            Assert.Null(result);
        }
    }
}
