using System.Collections.Generic;
using System.Globalization;
using Aggregates.Contracts;

namespace Aggregates.Extensions
{
    static class DescriptorExtensions
    {
        public static IDictionary<string, string> ToDictionary(this IEventDescriptor descriptor)
        {
            var result = new Dictionary<string, string>
            {
                ["Version"] = descriptor.Version.ToString(),
                ["EntityType"] = descriptor.EntityType,
                ["Timestamp"] = descriptor.Timestamp.ToString(CultureInfo.InvariantCulture)
            };


            return descriptor.Headers.Merge(result);
        }
    }
}