using Aggregates.Contracts;
using Newtonsoft.Json;

namespace Aggregates.Extensions
{
    public static class StoreExtensions
    {

        public static string Serialize(this ISnapshot snapshot, JsonSerializerSettings settings)
        {
            return JsonConvert.SerializeObject(snapshot, settings);
        }
        
    }
}