using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Aggregates.Internal;
using Newtonsoft.Json;

namespace Aggregates.Extensions
{
    static class StoreExtensions
    {
        public static byte[] AsByteArray(this string json)
        {
            return Encoding.UTF8.GetBytes(json);
        }
        public static byte[] Compress(this byte[] bytes)
        {
            using (var stream = new MemoryStream())
            {
                using (var zip = new GZipStream(stream, CompressionMode.Compress))
                {
                    using (var writer = new BinaryWriter(zip, Encoding.UTF8))
                    {
                        writer.Write(bytes.Length);
                        writer.Write(bytes, 0, bytes.Length);
                    }
                }
                return stream.ToArray();
            }
        }
        public static byte[] Decompress(this byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
            {
                using (var gz = new GZipStream(stream, CompressionMode.Decompress))
                {
                    using (var reader = new BinaryReader(gz, Encoding.UTF8))
                    {
                        var length = reader.ReadInt32();
                        return reader.ReadBytes(length);
                    }
                }
            }
        }

        public static string Serialize(this object @event, JsonSerializerSettings settings)
        {
            return JsonConvert.SerializeObject(@event, settings);
        }

        public static string Serialize(this EventDescriptor descriptor, JsonSerializerSettings settings)
        {
            return JsonConvert.SerializeObject(descriptor, settings);
        }

        public static object Deserialize(this byte[] bytes, string type, JsonSerializerSettings settings)
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
            return JsonConvert.DeserializeObject<T>(json, settings);
        }

        public static string ToLowerCamelCase(this string type)
        {
            // Unsure if I want to trim the namespaces or not
            var name = type.Substring(type.LastIndexOf('.') + 1);
            return char.ToLower(name[0]) + name.Substring(1);
        }

    }
}
