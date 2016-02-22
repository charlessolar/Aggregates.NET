using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Internal;
using EventStore.ClientAPI;
using Newtonsoft.Json;
using NServiceBus.Logging;

namespace Aggregates.Extensions
{
    public static class StoreExtensions
    {

        public static String Serialize(this ISnapshot snapshot, JsonSerializerSettings settings)
        {
            return JsonConvert.SerializeObject(snapshot, settings);
        }
        
    }
}