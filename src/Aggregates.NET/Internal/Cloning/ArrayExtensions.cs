using System;

namespace Aggregates.Internal.Cloning
{
    /// <summary>
    ///     Array datastructure extensions
    /// </summary>
    static class ArrayExtensions
    {
        /// <summary>
        ///     Iterate through all array elements executing a given action for each array item
        /// </summary>
        /// <param name="array">The array.</param>
        /// <param name="action">The action.</param>
        public static void ForEach(this Array array, Action<Array, int[]> action)
        {
            if (array.LongLength == 0) return;
            var walker = new ArrayTraverse(array);
            do action(array, walker.Position); while (walker.Step());
        }
    }
}