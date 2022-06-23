using Aggregates.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class ServiceCollectionExtensions
    {
        public static IHostBuilder AddAggregatesNet(this IHostBuilder builder, Action<Settings> settings)
        {

            builder.ConfigureServices((context, collection) =>
            {
                Configuration.Build(collection, settings).Wait();
                collection.AddHostedService<HostedService>();
            });

            return builder;
        }
    }
}
