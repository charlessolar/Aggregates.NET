using System;
using System.Collections.Generic;
using System.Text;
using Aggregates.Messages;

namespace Aggregates.Contracts
{
    public interface IState
    {
        Id Id { get; set;  }
        string Bucket { get; set; }
        Id[] Parents { get; set; }

        long Version { get; set;  }

        IState Snapshot { get; set;  }
        
        void Conflict(IEvent @event);
        void Apply(IEvent @event);

        bool ShouldSnapshot();
    }
}
