using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class SIConfigure
    {
        public static Configure SimpleInjector(this Configure config, SimpleInjector.Container container)
        {
            config.Container = new Internal.Container(container);
            return config;
        }
    }
}
