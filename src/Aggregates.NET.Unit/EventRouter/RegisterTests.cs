using Aggregates.Contracts;
using Aggregates.Internal;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.EventRouter
{
    [TestFixture]
    public class RegisterTests
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
        public void register_one_route()
        {
            _resolver.Setup(x => x.Resolve(Moq.It.IsAny<Aggregate<Guid>>())).Returns(new Dictionary<Type, Action<Object>>{ { typeof(String), a => {}}});
            //Assert.DoesNotThrow(() => _router.Register(Moq.It.IsAny<Aggregate<Guid>>()));
        }

        [Test]
        public void register_two_same_route()
        {
            _resolver.Setup(x => x.Resolve(Moq.It.IsAny<Aggregate<Guid>>())).Returns(new Dictionary<Type, Action<Object>> { { typeof(String), a => { } } });
            //Assert.DoesNotThrow(() => _router.Register(Moq.It.IsAny<Aggregate<Guid>>()));
            //Assert.DoesNotThrow(() => _router.Register(Moq.It.IsAny<Aggregate<Guid>>()));
        }

        [Test]
        public void register_single_handle()
        {
            //Assert.DoesNotThrow(() => _router.Register(typeof(String), a => { }));
        }

        [Test]
        public void register_two_same_route_single_handle()
        {
            //Assert.DoesNotThrow(() => _router.Register(typeof(String), a => { }));
            //Assert.DoesNotThrow(() => _router.Register(typeof(String), a => { }));
        }
    }
}
