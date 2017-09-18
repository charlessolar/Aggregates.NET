using System;
using System.Collections.Generic;
using System.Text;
using Aggregates.Messages;

namespace Aggregates.Contracts
{
    interface IMutateState
    {
        void Handle(IState state, IEvent @event);
        void Conflict(IState state, IEvent @event);
    }
}
