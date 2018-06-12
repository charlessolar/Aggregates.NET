using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class RealRandomProvider : IRandomProvider
    {
        private Random _random;

        public RealRandomProvider()
        {
            _random = new Random();
        }
        public bool Chance(int percent)
        {
            return _random.Next(100) <= percent;
        }
    }
}
