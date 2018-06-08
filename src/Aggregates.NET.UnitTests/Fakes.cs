using Aggregates.Contracts;
using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    public class FakeState : State<FakeState>
    {

    }

    public class Fakes : Entity<Fakes, FakeState>
    {
        private Fakes() { }
    }

    public class FakeChild : Entity<FakeChild, FakeState, Fakes>
    {
        private FakeChild() { }

    }
}
