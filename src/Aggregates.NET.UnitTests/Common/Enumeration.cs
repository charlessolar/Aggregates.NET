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
    public class Enumeration : Test
    {
        [Fact]
        public void ShouldBeEqual()
        {
            var a = FakeEnumeration.One;
            var b = FakeEnumeration.One;
            (a == b).Should().BeTrue();
            (a != b).Should().BeFalse();
        }
        [Fact]
        public void ShouldBeNotEqual()
        {
            var a = FakeEnumeration.One;
            var b = FakeEnumeration.Two;
            (a == b).Should().BeFalse();
            (a != b).Should().BeTrue();
        }
        [Fact]
        public void ShouldCompareToUsingValue()
        {
            var a = FakeEnumeration.One;
            var b = FakeEnumeration.Two;
            a.CompareTo(b).Should().Be(a.Value.CompareTo(b.Value));
        }
        [Fact]
        public void ShouldCompareToNullBeGreaterThanNull()
        {
            var a = FakeEnumeration.One;
            a.CompareTo(default(FakeEnumeration)).Should().BeGreaterThan(0);
        }
        [Fact]
        public void ShouldCompareToBe0ForEqualInstances()
        {
            var a = FakeEnumeration.One;
            var b = FakeEnumeration.One;
            a.CompareTo(b).Should().Be(0);
        }
        [Fact]
        public void ShouldParseUsingDisplayName()
        {
            FakeEnumeration temp;
            FakeEnumeration.Parse("one").Should().Be(FakeEnumeration.One);
            FakeEnumeration.TryParse("one", out temp).Should().BeTrue();
        }
        [Fact]
        public void ShouldParseUsingFromValue()
        {
            FakeEnumeration temp;
            FakeEnumeration.FromValue(1).Should().Be(FakeEnumeration.One);
            FakeEnumeration.TryParse(1, out temp).Should().BeTrue();
        }
        [Fact]
        public void ShouldThrowArgumentExceptionWhenBadDisplayName()
        {
            FakeEnumeration temp;
            var e = Record.Exception(() => FakeEnumeration.Parse("test"));
            e.Should().BeOfType<ArgumentException>();
            e.Message.Should().Contain(typeof(FakeEnumeration).FullName);
            e.Message.Should().Contain("test");
            FakeEnumeration.TryParse("temp", out temp).Should().BeFalse();
        }
        [Fact]
        public void ShouldThrowArgumentExceptionWhenBadValue()
        {
            FakeEnumeration temp;
            var e = Record.Exception(() => FakeEnumeration.FromValue(-1));
            e.Should().BeOfType<ArgumentException>();
            e.Message.Should().Contain((-1).ToString());
            e.Message.Should().Contain(typeof(FakeEnumeration).FullName);
            FakeEnumeration.TryParse(-1, out temp).Should().BeFalse();
        }
        [Fact]
        public void ShouldSortList()
        {
            var enums = new[] { FakeEnumeration.One, FakeEnumeration.Two, FakeEnumeration.One, FakeEnumeration.Two };
            Array.Sort(enums);

            enums.ElementAt(0).Should().Be(FakeEnumeration.One);
            enums.ElementAt(1).Should().Be(FakeEnumeration.One);
            enums.ElementAt(2).Should().Be(FakeEnumeration.Two);
            enums.ElementAt(3).Should().Be(FakeEnumeration.Two);
        }
        [Fact]
        public void ShouldHasDisplayName()
        {
            FakeEnumeration.HasDisplayName("one").Should().BeTrue();
            FakeEnumeration.HasDisplayName("test").Should().BeFalse();
        }
        [Fact]
        public void ShouldHasValue()
        {
            FakeEnumeration.HasValue(1).Should().BeTrue();
            FakeEnumeration.HasValue(-1).Should().BeFalse();
        }
        [Fact]
        public void ShouldToStringBeDisplayName()
        {
            FakeEnumeration.One.ToString().Should().Be(FakeEnumeration.One.DisplayName);
        }
    }
}
