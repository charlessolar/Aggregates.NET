using FluentAssertions;
using Xunit;

namespace Aggregates.Testing
{
    public class TestableId : TestSubject<Internal.IdRegistry>
    {
        [Fact]
        public void TwoAnyIdEqual()
        {
            var id1 = Sut.AnyId();
            var id2 = Sut.AnyId();
            id1.Should().Be(id2);
        }

        [Fact]
        public void TwoIntKeysDifferent() {
            var id1 = Sut.MakeId(1);
            var id2 = Sut.MakeId(2);
            id1.Should().NotBe(id2);
        }
        [Fact]
        public void TwoSameIntSameId() {
            var id1 = Sut.MakeId(1);
            var id2 = Sut.MakeId(1);
            id1.Should().Be(id2);
        }
        [Fact]
        public void TwoStringKeysDifferent() {
            var id1 = Sut.MakeId("1");
            var id2 = Sut.MakeId("2");
            id1.Should().NotBe(id2);
        }
        [Fact]
        public void TwoSameStringsSameId() {
            var id1 = Sut.MakeId("1");
            var id2 = Sut.MakeId("1");
            id1.Should().Be(id2);
        }

    }
}
