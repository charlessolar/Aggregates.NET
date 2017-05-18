using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public struct OobDefinition
    {
        public string Id { get; set; }
        public bool? Transient { get; set; }
        public int? DaysToLive { get; set; }
    }
}
