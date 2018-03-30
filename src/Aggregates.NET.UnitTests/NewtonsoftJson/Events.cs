using Aggregates.Contracts;
using Aggregates.Internal;
using Aggregates.Extensions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.NewtonsoftJson
{
    [TestFixture]
    public class Events
    {
        interface ISimple
        {
            string Test { get; set; }
        }

        class Simple_impl : ISimple
        {
            public string Test { get; set; }
        }

        interface IContain
        {
            object Event { get; set; }
        }

        class Contain_impl : IContain
        {
            public object Event { get; set; }
        }

        private Moq.Mock<IEventMapper> _mapper;
        private Moq.Mock<IEventFactory> _factory;

        private JsonMessageSerializer _serializer;

        [SetUp]
        public void Setup()
        {
            _mapper = new Moq.Mock<IEventMapper>();
            _factory = new Moq.Mock<IEventFactory>();

            _mapper.Setup(x => x.GetMappedTypeFor(typeof(ISimple))).Returns(typeof(Simple_impl));
            _mapper.Setup(x => x.GetMappedTypeFor(typeof(Simple_impl))).Returns(typeof(ISimple));
            _mapper.Setup(x => x.GetMappedTypeFor(typeof(IContain))).Returns(typeof(Contain_impl));
            _mapper.Setup(x => x.GetMappedTypeFor(typeof(Contain_impl))).Returns(typeof(IContain));

            _factory.Setup(x => x.Create(typeof(ISimple))).Returns(new Simple_impl());
            _factory.Setup(x => x.Create(typeof(Simple_impl))).Returns(new Simple_impl());
            _factory.Setup(x => x.Create(typeof(IContain))).Returns(new Contain_impl());
            _factory.Setup(x => x.Create(typeof(Contain_impl))).Returns(new Contain_impl());

            _serializer = new JsonMessageSerializer(_mapper.Object, _factory.Object, new Newtonsoft.Json.JsonConverter[] { });
        }

        [Test]
        public void simple_object()
        {
            ISimple obj = new Simple_impl { Test = "test" };

            var serialized = _serializer.Serialize(obj);

            var deserialized = _serializer.Deserialize<ISimple>(serialized);

            Assert.AreEqual(obj.Test, deserialized.Test);
        }

        [Test]
        public void contain_event()
        {
            IContain obj = new Contain_impl
            {
                Event = (ISimple) new Simple_impl {Test = "test"}
            };

            var serialized = _serializer.Serialize(obj);

            var deserialized = _serializer.Deserialize<IContain>(serialized);

            Assert.IsTrue(deserialized.Event is ISimple);
            Assert.AreEqual((deserialized.Event as ISimple).Test, (obj.Event as ISimple).Test);
        }
    }
}
