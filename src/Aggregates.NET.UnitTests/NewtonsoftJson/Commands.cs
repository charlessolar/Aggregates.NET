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
    public class Commands
    {
        class Simple
        {
            public string Test { get; set; }
        }

        class Contain
        {
            public object Command { get; set; }
        }
        

        private Moq.Mock<IEventMapper> _mapper;
        private Moq.Mock<IEventFactory> _factory;

        private JsonMessageSerializer _serializer;

        [SetUp]
        public void Setup()
        {
            _mapper = new Moq.Mock<IEventMapper>();
            _factory = new Moq.Mock<IEventFactory>();

            _serializer = new JsonMessageSerializer(_mapper.Object, _factory.Object, new Newtonsoft.Json.JsonConverter[] { });
        }

        [Test]
        public void simple_object()
        {
            Simple obj = new Simple { Test = "test" };

            var serialized = _serializer.Serialize(obj);

            var deserialized = _serializer.Deserialize<Simple>(serialized);

            Assert.AreEqual(obj.Test, deserialized.Test);
        }

        [Test]
        public void contain_command()
        {
            Contain obj = new Contain
            {
                Command = (Simple)new Simple { Test = "test" }
            };

            var serialized = _serializer.Serialize(obj);

            var deserialized = _serializer.Deserialize<Contain>(serialized);

            Assert.IsTrue(deserialized.Command is Simple);
            Assert.AreEqual((deserialized.Command as Simple).Test, (obj.Command as Simple).Test);
        }
    }
}
