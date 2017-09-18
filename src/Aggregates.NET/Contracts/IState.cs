using System;
using System.Collections.Generic;
using System.Text;
using Aggregates.Messages;

namespace Aggregates.Contracts
{
    public interface IState
    {
        long Version { get; }

        IState Snapshot { get; }
        
        void Conflict(IEvent @event);
        void Apply(IEvent @event);

        bool ShouldSnapshot();
        void RestoreSnapshot(IState state);
    }
}
