namespace Aggregates.Contracts
{
    public interface ISnapshotting
    {
        int? LastSnapshot { get; }

        void RestoreSnapshot(object snapshot);
        object TakeSnapshot();
        bool ShouldTakeSnapshot();
    }
}