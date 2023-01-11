using Aggregates.Contracts;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class RealRandomProvider : IRandomProvider
    {
        private readonly Random _random;

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
