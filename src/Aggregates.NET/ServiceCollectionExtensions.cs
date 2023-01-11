using Aggregates.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics.CodeAnalysis;

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
        public static IHostBuilder AddAggregatesNet(this IHostBuilder builder, Action<HostBuilderContext, Settings> settings)
        {

            builder.ConfigureServices((context, collection) =>
            {
                Configuration.Build(collection, x => settings(context, x)).Wait();
                collection.AddHostedService<HostedService>();
            });

            return builder;
        }
    }
}
