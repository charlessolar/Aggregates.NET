using Aggregates.Contracts;
using Aggregates.Internal;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
        public static byte[] Compress(this byte[] bytes)
        {
            using (var stream = new MemoryStream())
            {
                using (var zip = new GZipStream(stream, CompressionMode.Compress))
                {
                    zip.Write(bytes, 0, bytes.Length);
                }
                return stream.ToArray();
            }
        }
        public static byte[] Decompress(this byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
            {
                using (var dezip = new MemoryStream())
                {
                    using (var gz = new GZipStream(stream, CompressionMode.Decompress))
                    {
                        gz.CopyTo(dezip);
                    }
                    return dezip.ToArray();
                }
            }
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
            var resolved = Type.GetType(type, false);
            if (resolved == null) return null;

            return JsonConvert.DeserializeObject(json, resolved, settings);
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
        
    }
}
