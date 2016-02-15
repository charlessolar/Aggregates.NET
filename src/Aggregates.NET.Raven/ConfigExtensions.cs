using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.NET.Raven
{
    public static class ConfigExtensions
    {
        public static void WithRavenSnapshots(this Aggregates.ConfigExtensions.Config config)
        {
            config.Bus.EnableFeature<Feature>();
        }
    }
}
