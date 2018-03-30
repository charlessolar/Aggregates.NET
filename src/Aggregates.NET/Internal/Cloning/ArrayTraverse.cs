using System;

namespace Aggregates.Internal.Cloning
{
    //Assist in foreach array traversal
    internal class ArrayTraverse
    {
        #region Fields

        private readonly int[] maxLengths;
        public int[] Position;

        #endregion

        #region Ctors

        public ArrayTraverse(Array array)
        {
            this.maxLengths = new int[array.Rank];
            for (var i = 0; i < array.Rank; ++i)
            {
                this.maxLengths[i] = array.GetLength(i) - 1;
            }
            this.Position = new int[array.Rank];
        }

        #endregion

        #region Methods

        public bool Step()
        {
            for (var i = 0; i < this.Position.Length; ++i)
            {
                if (this.Position[i] < this.maxLengths[i])
                {
                    this.Position[i]++;
                    for (var j = 0; j < i; j++)
                    {
                        this.Position[j] = 0;
                    }
                    return true;
                }
            }
            return false;
        }

        #endregion
    }
}