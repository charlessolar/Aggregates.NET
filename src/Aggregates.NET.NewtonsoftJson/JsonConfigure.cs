using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Aggregates.Contracts;
using Aggregates.Internal;
using System.Threading.Tasks;

namespace Aggregates
{
    public static class JsonConfigure
    {
        public static Configure NewtonsoftJson(this Configure config)
        {
            config.SetupTasks.Add(() =>
            {
                var container = Configuration.Settings.Container;

                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    Converters = new JsonConverter[] { new Newtonsoft.Json.Converters.StringEnumConverter(), new Internal.IdJsonConverter() },
                };
                
                container.Register<IMessageSerializer>((factory) => new JsonMessageSerializer(factory.Resolve<IEventMapper>(), null, null, settings, null));

                return Task.CompletedTask;
            });
            return config;
        }
    }
}
