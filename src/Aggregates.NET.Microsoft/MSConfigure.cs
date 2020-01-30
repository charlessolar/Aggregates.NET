using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class MSConfigure
    {
        public static Configure Microsoft(this Configure config, IServiceCollection serviceCollection)
        {
            config.Container = new Internal.ServiceCollection(serviceCollection);
            return config;
        }
        // pass a completed provider into the container object as we've finished "registration" phase and need to start the bus
        // todo: this makes using micrsoft DI weird and clunky
        public static Task MicrosoftStart(IServiceProvider provider)
        {
            Configuration.Settings.Container = new Internal.ServiceProvider(provider);
            return Configuration.Start();
        }
    }
}
