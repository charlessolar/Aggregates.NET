using System;
using System.Collections.Generic;
using System.Text;
using Aggregates.Internal;
using Aggregates.Messages;

namespace Aggregates.Contracts
{
    public interface IEntity
    {
        Id Id { get; }
        string Bucket { get; }

        long Version { get; }
        long StateVersion { get; }
        bool Dirty { get; }
        IFullEvent[] Uncommitted { get; }
    }

    public interface IEntity<TState> : IEntity where TState : IState, new()
    {
        void Instantiate(TState state);
        void Snapshotting();
        TState State { get; }

        void Conflict(IEvent @event);
        void Apply(IEvent @event);
        void Raise(IEvent @event, string id, bool transient = false, int? daysToLive = null, bool? single = null);
    }
    public interface IChildEntity : IEntity
    {
        IEntity Parent { get; }
    }
    public interface IChildEntity<out TParent> : IChildEntity where TParent : IEntity
    {
        new TParent Parent { get; }
    }
}
