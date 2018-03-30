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
    class UnknownType
    {
        interface IFooBar
        {
            string Test { get; set; }
        }
        class FooBar : IFooBar
        {
            public string Test { get; set; }
        }
        
        class Contain
        {
            public IFooBar Unknown { get; set; }
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
        public void contain_unknown()
        {
            Contain obj = new Contain
            {
                Unknown = (IFooBar)new FooBar { Test = "test" }
            };

            var serialized = _serializer.Serialize(obj);

            var deserialized = _serializer.Deserialize<Contain>(serialized);
            
            Assert.AreEqual(deserialized.Unknown.Test, obj.Unknown.Test);
        }
    }
}
