namespace Aggregates.Contracts
{
    public interface ISnapshotting
    {
        ISnapshot Snapshot { get; }

        void RestoreSnapshot(IMemento snapshot);
        IMemento TakeSnapshot();
        bool ShouldTakeSnapshot();
    }
}