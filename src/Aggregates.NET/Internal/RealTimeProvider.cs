using Aggregates.Contracts;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class RealTimeProvider : ITimeProvider
    {
        public DateTime Now => DateTime.UtcNow;
    }
}
