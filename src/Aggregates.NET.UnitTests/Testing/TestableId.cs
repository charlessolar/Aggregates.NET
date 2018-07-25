using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Aggregates.Testing
{
    public class TestableId : TestSubject<Internal.TestableId>
    {
        [Fact]
        public void ShouldNotChangeId()
        {
            
        }
    }
}
