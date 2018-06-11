using Aggregates.Contracts;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common
{
    public class IdTests : Test
    {
        [Fact]
        public void ShouldCreateIdFromString()
        {
            Aggregates.Id id = "test";
            string test = id;
        }
        [Fact]
        public void ShouldCreateIdFromGuid()
        {
            Aggregates.Id id = Guid.NewGuid();
            Guid test = id;
            Guid? test2 = id;
        }
        [Fact]
        public void ShouldCreateIdFromLong()
        {
            Aggregates.Id id = 1L;
            long test = id;
            long? test2 = id;
        }
        [Fact]
        public void TwoSameIdShouldBeEqual()
        {
            Aggregates.Id id = "test";
            id.Equals(id).Should().BeTrue();
        }
        [Fact]
        public void IdShouldNotEqualNull()
        {
            Aggregates.Id id = "test";
            id.Equals(null).Should().BeFalse();
        }
        [Fact]
        public void NullIdShouldEqualNull()
        {
            Aggregates.Id id = (string)null;
            id.Equals(null).Should().BeTrue();
        }
        [Fact]
        public void TwoIdenticalIdsShouldEqual()
        {
            Aggregates.Id id1 = "test";
            Aggregates.Id id2 = "test";

            id1.Equals(id2).Should().BeTrue();
        }
        [Fact]
        public void StringIdShouldEqualString()
        {
            Aggregates.Id id = "test";
            id.Equals("test").Should().BeTrue();
            (id == (Id)"test").Should().BeTrue();
            (id != (Id)"tt").Should().BeTrue();
        }
        [Fact]
        public void LongIdShouldEqualLong()
        {
            Aggregates.Id id = 1L;
            id.Equals(1L).Should().BeTrue();
            (id == (Id)1L).Should().BeTrue();
            (id != (Id)2L).Should().BeTrue();
        }
        [Fact]
        public void GuidIdShouldEqualGuid()
        {
            var guid = Guid.NewGuid();
            Aggregates.Id id = guid;
            id.Equals(guid).Should().BeTrue();
            (id == (Id)guid).Should().BeTrue();
            (id != (Id)Guid.NewGuid()).Should().BeTrue();
        }
    }
}
