using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    public static class SMConfigure
    {
        public static Configure StructureMap(this Configure config, StructureMap.IContainer container)
        {
            config.Container = new Internal.Container(container);
            return config;
        }
    }
}
