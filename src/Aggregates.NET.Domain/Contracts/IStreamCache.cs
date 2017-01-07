namespace Aggregates.Contracts
{
    public interface IStreamCache
    {
        //void Cache(String stream, IEventStream eventstream);
        //IEventStream Retreive(String stream);

        //void CacheSnap(String stream, ISnapshot snapshot);
        //ISnapshot RetreiveSnap(String stream);

        void Cache(string stream, object cached, bool expires10S = false, bool expires1M = false);
        object Retreive(string stream);

        void Evict(string stream);
    }
}
