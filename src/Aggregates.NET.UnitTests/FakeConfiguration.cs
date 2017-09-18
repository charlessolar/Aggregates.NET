using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.UnitTests
{
    class FakeConfiguration : Aggregates.Configure
    {
        public Moq.Mock<IContainer> FakeContainer;

        public FakeConfiguration() : base()
        {
            FakeContainer = new Moq.Mock<IContainer>();
            FakeContainer.Setup(x => x.GetChildContainer()).Returns(FakeContainer.Object);
            Container = FakeContainer.Object;
        }
    }
}
