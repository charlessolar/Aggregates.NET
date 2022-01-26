using System;
using System.Collections.Generic;
using System.Text;
using Aggregates.Internal;
using Aggregates.Messages;
using Microsoft.Extensions.Logging;

namespace Aggregates.Contracts
{
    public interface IState : IEvent
    {
        Id Id { get; set;  }
        string Bucket { get; set; }
        IParentDescriptor[] Parents { get; set; }

        long Version { get; set;  }

        IState Snapshot { get; set;  }
        IEvent[] Committed { get; }

        ILogger Logger { get; set; }
        
        void Conflict(IEvent @event);
        void Apply(IEvent @event);

        void SnapshotRestored();
        void Snapshotting();
        bool ShouldSnapshot();
    }
}
