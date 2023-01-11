using Aggregates.Extensions;
using FluentAssertions;
using Xunit;

namespace Aggregates.Common.Extensions
{
    public class String : Test
    {
        [Fact]
        public void ShouldTrimLongString()
        {
            var test = "test";
            test = test.MaxLength(1);
            test.Should().Be("t");
        }
        [Fact]
        public void ShouldTrimNumberOfLines()
        {
            var test = @"one
                        two
                        three";
            test = test.MaxLines(1);
            test.Should().Be("one");
        }
        [Fact]
        public void ShouldNotTrimShortString()
        {
            var test = "test";
            test = test.MaxLength(10);
            test.Should().Be("test");
        }
        [Fact]
        public void ShouldNotTrimNumberOfLines()
        {
            var test = @"one";
            test = test.MaxLines(4);
            test.Should().Be("one");
        }
        [Fact]
        public void ShouldNotTrimNumberOfLinesOnMultilineString()
        {
            var test = "one\ntwo";
            test = test.MaxLines(4);
            test.Should().Be("one\ntwo");
        }
        [Fact]
        public void ShouldGetAStringHash()
        {
            var test = "test";
            test.GetHash();
        }
    }
}
