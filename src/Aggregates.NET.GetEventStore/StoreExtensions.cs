using Aggregates.Internal;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Extensions
{
    public static class StoreExtensions
    {
        public static byte[] AsByteArray(this String json)
        {
            return Encoding.UTF8.GetBytes(json);
        }

        public static String Serialize(this Object @event, JsonSerializerSettings settings)
        {
            return JsonConvert.SerializeObject(@event, settings);
        }

        public static String Serialize(this EventDescriptor descriptor, JsonSerializerSettings settings)
        {
            return JsonConvert.SerializeObject(descriptor, settings);
        }

        public static Object Deserialize(this byte[] bytes, string type, JsonSerializerSettings settings)
        {
            var json = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject(json, Type.GetType(type), settings);
        }
        public static EventDescriptor Deserialize(this byte[] bytes, JsonSerializerSettings settings)
        {
            var json = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject<EventDescriptor>(json, settings);
        }
        public static T Deserialize<T>(this byte[] bytes, JsonSerializerSettings settings)
        {
            var json = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject<T>(json);
        }

        public static String toLowerCamelCase(this String type)
        {
            // Unsure if I want to trim the namespaces or not
            var name = type.Substring(type.LastIndexOf('.') + 1);
            return char.ToLower(name[0]) + name.Substring(1);
        }

        public static T WaitForResult<T>(this Task<T> task)
        {
            task.Wait();
            return task.Result;
        }
    }
}