using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Aggregates.Contracts;
using Aggregates.Internal;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using System.Net.Mime;
using Microsoft.Extensions.DependencyInjection;

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
                container.AddSingleton<IMessageSerializer>((factory) => new JsonMessageSerializer(factory.GetRequiredService<IEventMapper>(), factory.GetRequiredService<IEventFactory>(), extraConverters));

                return Task.CompletedTask;
            });
            return config;
        }
    }
}
