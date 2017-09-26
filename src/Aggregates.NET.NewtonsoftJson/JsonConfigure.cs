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
            config.RegistrationTasks.Add((c) =>
            {
                var container = c.Container;
                                
                container.RegisterSingleton<IMessageSerializer>((factory) => new JsonMessageSerializer(factory.Resolve<IEventMapper>(), null, null, null, null));

                return Task.CompletedTask;
            });
            return config;
        }
    }
}
