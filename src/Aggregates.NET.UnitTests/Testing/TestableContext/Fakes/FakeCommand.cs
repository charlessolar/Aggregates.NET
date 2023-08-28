using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Testing.TestableContext.Fakes {
    public class FakeCommand {
        public string EntityId { get; set; }
        public bool RaiseEvent { get; set; }
        public string Content { get; set; }
    }
}
