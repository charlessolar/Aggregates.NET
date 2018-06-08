using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    class FakeState : State<FakeState>
    {

    }

    class FakeEntity : Entity<FakeEntity, FakeState>
    {
    }

    class FakeChild : Entity<FakeChild, FakeState, FakeEntity>
    {

    }
}
