using Aggregates.Contracts;
using Aggregates.Internal;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;
using System.Net.Mime;
using System.Threading.Tasks;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class JsonConfigure
    {
        public static Settings NewtonsoftJson(this Settings config, JsonConverter[] extraConverters = null)
        {
            extraConverters = extraConverters ?? new JsonConverter[] { };

            config.MessageContentType = MediaTypeNames.Application.Json;

            Settings.RegistrationTasks.Add((container, settings) =>
            {
                container.AddSingleton<IMessageSerializer>((factory) => new JsonMessageSerializer(factory.GetRequiredService<IEventMapper>(), factory.GetRequiredService<IEventFactory>(), extraConverters, config.DevelopmentMode));

                return Task.CompletedTask;
            });
            return config;
        }
    }
}
