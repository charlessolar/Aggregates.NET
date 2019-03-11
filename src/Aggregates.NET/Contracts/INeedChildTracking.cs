using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Contracts
{
    interface INeedChildTracking
    {
        ITrackChildren Tracker { get; set; }
    }
}
