using Aggregates.Contracts;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common.Exceptions
{
    public class BusinessException : Test
    {
        [Fact]
        public void ShouldConstruct()
        {
            var e = new Aggregates.BusinessException();
            e.Message.Should().NotBeEmpty();
        }
        [Fact]
        public void ShouldContainRuleFailure()
        {
            var e = new Aggregates.BusinessException("rule");
            e.Message.Should().Contain("rule");
        }
        [Fact]
        public void ShouldContainRuleAndMessage()
        {
            var e = new Aggregates.BusinessException("rule", "message");
            e.Rule.Should().Be("rule");
            e.Message.Should().ContainAll("rule", "message");
        }
        [Fact]
        public void ShouldBeSerializable()
        {
            var business = new Aggregates.BusinessException("rule", "message");

            MemoryStream mem = new MemoryStream();
            BinaryFormatter b = new BinaryFormatter();

            var e = Record.Exception(() => b.Serialize(mem, business));
            e.Should().BeNull();
        }
        [Fact]
        public void ShouldBeDeserializable()
        {
            var business = new Aggregates.BusinessException("rule", "message");

            MemoryStream mem = new MemoryStream();
            BinaryFormatter b = new BinaryFormatter();
            b.Serialize(mem, business);

            mem.Position = 0;

            var e = Record.Exception(() => b.Deserialize(mem));

            e.Should().BeNull();
        }
        [Fact]
        public void ShouldDeserialize()
        {
            var business = new Aggregates.BusinessException("rule", "message");

            MemoryStream mem = new MemoryStream();
            BinaryFormatter b = new BinaryFormatter();
            b.Serialize(mem, business);

            mem.Position = 0;

            var deserialized = b.Deserialize(mem) as Aggregates.BusinessException;
            deserialized.Rule.Should().Be("rule");
            deserialized.Message.Should().ContainAll("rule", "message");
        }
    }
}
