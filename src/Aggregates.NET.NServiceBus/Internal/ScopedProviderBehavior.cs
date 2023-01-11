using Microsoft.Extensions.DependencyInjection;
using NServiceBus.Pipeline;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    internal class ScopedProviderBehavior : Behavior<IIncomingPhysicalMessageContext>
    {
        private readonly IServiceProvider _provider;
        private readonly ISettings _settings;
        public ScopedProviderBehavior(IServiceProvider provider, ISettings settings)
        {
            _provider = provider;
            _settings = settings;
        }

        public override async Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            using (var child = _provider.CreateScope())
            {
                context.Extensions.Set(child);
                context.Extensions.Set(child.ServiceProvider);

                context.Extensions.Set(_settings);
                context.Extensions.Set(_settings.Configuration);

                await next().ConfigureAwait(false);
            }
        }
    }
    [ExcludeFromCodeCoverage]
    internal class ScopedProviderRegistration : RegisterStep
    {
        public ScopedProviderRegistration() : base(
            stepId: "ScopedProvider",
            behavior: typeof(ScopedProviderBehavior),
            description: "Provides a scoped service provider",
            factoryMethod: (b) => new ScopedProviderBehavior(b.GetService<IServiceProvider>(), b.GetService<ISettings>())
        )
        {
            InsertBefore("MutateIncomingTransportMessage");
        }
    }
}
