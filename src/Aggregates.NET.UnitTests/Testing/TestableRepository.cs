using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Testing {
    public class TestableRepository : TestSubject<Internal.TestableRepository<FakeEntity, FakeState>> {
    }
}
