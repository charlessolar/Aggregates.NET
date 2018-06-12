using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class RealTimeProvider : ITimeProvider
    {
        public DateTime Now => DateTime.UtcNow;
    }
}
