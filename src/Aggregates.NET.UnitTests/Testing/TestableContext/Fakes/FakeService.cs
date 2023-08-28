using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Testing.TestableContext.Fakes {
    public class FakeService : IService<FakeResponse> {
        public string Content { get; set; }
    }
    public class FakeResponse { 
        public string Content { get; set; }
    }
}
