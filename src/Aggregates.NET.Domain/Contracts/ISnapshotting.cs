namespace Aggregates.Contracts
{
    public interface ISnapshotting
    {
        ISnapshot Snapshot { get; }

        void RestoreSnapshot(object snapshot);
        object TakeSnapshot();
        bool ShouldTakeSnapshot();
    }
}