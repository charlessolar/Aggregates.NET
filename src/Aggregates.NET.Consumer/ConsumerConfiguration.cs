using NServiceBus;
using NServiceBus.Configuration.AdvanceExtensibility;
using NServiceBus.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{

    public static class ConsumerConfiguration
    {
        public static void SetEventStoreMaxDegreeOfParallelism( this ExposeSettings settings, Int32 Parallelism)
        {
            settings.GetSettings().Set("SetEventStoreMaxDegreeOfParallelism", Parallelism);
        }
        public static void SetEventStoreCapacity(this ExposeSettings settings, Int32 ProcessorCapacity, Int32 ParallelCapacity)
        {
            settings.GetSettings().Set("SetEventStoreCapacity", new Tuple<Int32, Int32>(ProcessorCapacity, ParallelCapacity));
        }
    }
}
