using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Aggregates.Contracts;
using Aggregates.Internal;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using System.Net.Mime;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class JsonConfigure
    {
        public static Configure NewtonsoftJson(this Configure config, JsonConverter[] extraConverters = null)
        {
            extraConverters = extraConverters ?? new JsonConverter[] { };

            config.MessageContentType = MediaTypeNames.Application.Json;
            config.RegistrationTasks.Add((c) =>
            {
                var container = c.Container;
                                
                container.Register<IMessageSerializer>((factory) => new JsonMessageSerializer(factory.Resolve<IEventMapper>(), factory.Resolve<IEventFactory>(), extraConverters), Lifestyle.Singleton);

                return Task.CompletedTask;
            });
            return config;
        }
    }
}