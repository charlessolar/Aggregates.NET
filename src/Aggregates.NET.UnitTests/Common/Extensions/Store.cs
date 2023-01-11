using Aggregates.Extensions;
using FluentAssertions;
using Xunit;

namespace Aggregates.Common.Extensions
{
    public class Store : Test
    {
        [Fact]
        public void ShouldConvertStringToByte()
        {
            var test = "test";
            var bytes = test.AsByteArray();
            var str = bytes.AsString();
            str.Should().Be("test");
        }
        [Fact]
        public void ShouldCompressAndDecompressBytes()
        {
            var test = "test";
            var bytes = test.AsByteArray();
            var compressed = bytes.Compress();
            var decompressed = compressed.Decompress();
            var str = decompressed.AsString();
            str.Should().Be("test");
        }
    }
}
