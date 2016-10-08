using System;

namespace Aggregates
{
    public interface IManageCompetes
    {
        /// <summary>
        /// Returns true if it saved the data (in other words if the domain has been unhandled)
        /// </summary>
        bool CheckOrSave(string endpoint, int bucket, long position);
        
        DateTime? LastHeartbeat(string endpoint, int bucket);
        long LastPosition(string endpoint, int bucket);
        void Heartbeat(string endpoint, int bucket, DateTime timestamp, long? position =null);
        bool Adopt(string endpoint, int bucket, DateTime timestamp);
    }
}
