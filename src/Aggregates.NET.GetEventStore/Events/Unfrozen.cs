using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Events
{
    public class Unfrozen
    {
        public DateTime Created { get; set; }
        public Guid Instance { get; set; }
    }
}
