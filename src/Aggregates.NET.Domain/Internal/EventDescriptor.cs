using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class EventDescriptor
    {
        public String EntityType { get; set; }

        public Int32 Version { get; set; }
        public DateTime Timestamp { get; set; }
        public String EventType { get; set; }

        public IDictionary<String, Object> Headers { get; set; }
    }
}