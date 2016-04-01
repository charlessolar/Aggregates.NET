using NServiceBus.Configuration.AdvanceExtensibility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public static class Configuration
    {
        public static void ShouldCacheEntities(this ExposeSettings settings, Boolean cache)
        {
            settings.GetSettings().Set("ShouldCacheEntities", cache);
        }
    }
}
