
using System;
using System.IO;

namespace Aggregates.Internal
{
    // https://github.com/Particular/NServiceBus.Newtonsoft.Json/blob/master/src/NServiceBus.Newtonsoft.Json/ReadonlyStream.cs
    class ReadOnlyStream : Stream
    {
        readonly ReadOnlyMemory<byte> memory;
        long position;

        public ReadOnlyStream(ReadOnlyMemory<byte> memory)
        {
            this.memory = memory;
            position = 0;
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    position = offset;
                    break;
                case SeekOrigin.Current:
                    position += offset;
                    break;
                case SeekOrigin.End:
                    position = memory.Length + offset;
                    break;
                default:
                    break;
            }

            return position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesToCopy = (int)Math.Min(count, memory.Length - position);

            var destination = buffer.AsSpan().Slice(offset, bytesToCopy);
            var source = memory.Span.Slice((int)position, bytesToCopy);

            source.CopyTo(destination);

            position += bytesToCopy;

            return bytesToCopy;
        }

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => memory.Length;
        public override long Position { get => position; set => position = value; }
    }
}
