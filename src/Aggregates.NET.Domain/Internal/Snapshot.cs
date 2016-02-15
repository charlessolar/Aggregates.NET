using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates;

namespace Aggregates.Internal
{
    public class Snapshot : ISnapshot
    {
        public String Bucket { get; set; }
        public String Stream { get; set; }
        public Int32 Version { get; set; }
        public Object Payload { get; set; }

        public String EntityType { get; set; }
        public DateTime Timestamp { get; set; }
    }
}