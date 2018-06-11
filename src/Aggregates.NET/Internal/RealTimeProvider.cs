using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Internal
{
    class RealTimeProvider : ITimeProvider
    {
        public DateTime Now => DateTime.UtcNow;
    }
}
