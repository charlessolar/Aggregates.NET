using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Extensions
{
    public static class DescriptorExtensions
    {
        public static IDictionary<String, String> ToDictionary(this IEventDescriptor descriptor)
        {
            var result = new Dictionary<String, String>();

            result["Version"] = descriptor.Version.ToString();
            result["EntityType"] = descriptor.EntityType;
            result["Timestamp"] = descriptor.Timestamp.ToString();

            return descriptor.Headers.ToDictionary(k => k.Key, v => v.Value).Merge(result);
        }
    }
}