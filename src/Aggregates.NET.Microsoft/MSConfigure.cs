using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class MSConfigure
    {
        public static Configure Microsoft(this Configure config, IServiceCollection serviceCollection, IServiceProvider provider)
        {
            config.Container = new Internal.Container(serviceCollection, provider);
            return config;
        }
    }
}
