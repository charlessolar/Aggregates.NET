using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Contracts
{
    public interface ITimeProvider
    {
        DateTime Now { get; }
    }
}
