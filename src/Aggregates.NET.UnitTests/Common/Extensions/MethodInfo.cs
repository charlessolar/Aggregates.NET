using Aggregates.Extensions;
using FluentAssertions;
using Xunit;

namespace Aggregates.Common.Extensions
{
    public class MethodInfo : Test
    {
        class FakeClass
        {
            public int One(int target) { return target; }
        }

        [Fact]
        public void ShouldCreateFunc()
        {
            var func = typeof(FakeClass).GetMethod("One").MakeFuncDelegateWithTarget<int, int>(typeof(FakeClass));
            func.Should().NotBeNull();
            func(new FakeClass(), 1).Should().Be(1);
        }
    }
}
