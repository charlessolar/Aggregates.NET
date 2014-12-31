using Aggregates.Contracts;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.EventRouter
{
    [TestFixture]
    public class GetTests
    {
        private Moq.Mock<IRouteResolver> _resolver;

        private IEventRouter _router;

        [SetUp]
        public void Setup()
        {
            _resolver = new Moq.Mock<IRouteResolver>();
            _router = new Aggregates.Internal.EventRouter(_resolver.Object);
        }

        [Test]
        public void get_no_routes_no_register()
        {
            Assert.Throws<HandlerNotFoundException>(() => _router.RouteFor(typeof(String)));
        }

        [Test]
        public void get_one_route()
        {
            _resolver.Setup(x => x.Resolve(Moq.It.IsAny<Aggregate<Guid>>())).Returns(new Dictionary<Type, Action<Object>> { { typeof(String), a => { } } });
            //_router.Register(Moq.It.IsAny<Aggregate<Guid>>());
            //Assert.DoesNotThrow(() => _router.RouteFor(typeof(String)));
        }

        [Test]
        public void get_unknown_route()
        {
            _resolver.Setup(x => x.Resolve(Moq.It.IsAny<Aggregate<Guid>>())).Returns(new Dictionary<Type, Action<Object>> { { typeof(String), a => { } } });
            //_router.Register(Moq.It.IsAny<Aggregate<Guid>>());
            //Assert.Throws<HandlerNotFoundException>(() => _router.RouteFor(typeof(Int32)));
        }

        [Test]
        public void get_multiple_registers()
        {
            //_resolver.Setup(x => x.Resolve(Moq.It.IsAny<Aggregate<Guid>>())).Returns(new Dictionary<Type, Action<Object>> { { typeof(String), a => { } } });
            //_router.Register(Moq.It.IsAny<Aggregate<Guid>>());
            //_resolver.Setup(x => x.Resolve(Moq.It.IsAny<Aggregate<Guid>>())).Returns(new Dictionary<Type, Action<Object>> { { typeof(Guid), a => { } } });
            //_router.Register(Moq.It.IsAny<Aggregate<Guid>>());
            //Assert.DoesNotThrow(() => _router.RouteFor(typeof(String)));
            //Assert.DoesNotThrow(() => _router.RouteFor(typeof(Guid)));
        }
    }
}
