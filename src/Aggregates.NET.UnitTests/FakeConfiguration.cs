using Aggregates.Contracts;
using FakeItEasy;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    class FakeConfiguration : Aggregates.Configure
    {
        public FakeConfiguration() : base()
        {
            
            Container = A.Fake<IContainer>();
            A.CallTo(() => Container.GetChildContainer()).Returns(Container);
        }
    }
}
