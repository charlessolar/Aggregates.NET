using System;
using System.Linq;

namespace Aggregates.Extensions
{
    // http://stackoverflow.com/questions/30404965/increment-guid-in-c-sharp#30405028
    public static class GuidExtensions
    {
        private static readonly int[] GuidByteOrder =
            {15, 14, 13, 12, 11, 10, 9, 8, 6, 7, 4, 5, 0, 1, 2, 3};

        public static Guid Increment(this Guid guid)
        {
            var bytes = guid.ToByteArray();
            var canIncrement = GuidByteOrder.Any(i => ++bytes[i] != 0);
            return new Guid(canIncrement ? bytes : new byte[16]);
        }

        public static Guid Add(this Guid guid, int add)
        {
            while (add > 0)
            {
                guid.Increment();
                add--;
            }
            return guid;
        }
    }
}
