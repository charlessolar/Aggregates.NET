namespace Aggregates.Contracts
{
    interface INeedChildTracking
    {
        ITrackChildren Tracker { get; set; }
    }
}
