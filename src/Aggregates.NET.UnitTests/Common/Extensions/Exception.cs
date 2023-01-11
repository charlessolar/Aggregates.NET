using Aggregates.Extensions;
using FluentAssertions;
using Xunit;

namespace Aggregates.Common.Extensions
{
    public class Exception : Test
    {
        [Fact]
        public void ShouldSerializeExceptionToString()
        {
            var e = new System.Exception("test");
            e.AsString().Should().Contain("test");
        }
        [Fact]
        public void ShouldSerializeInnerExceptionToString()
        {
            var e = new System.Exception("test", new System.Exception("test2"));
            e.AsString().Should().ContainAll("test", "test2");
        }
        [Fact]
        public void ShouldSerializeAggregateExceptionToString()
        {
            var e = new System.AggregateException("test", new[] {
                new System.Exception("test2"),
                new System.Exception("test3")
            });
            e.AsString().Should().ContainAll("test", "test2", "test3");
        }
    }
}
