using EventStore.ClientAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public interface IManageCompetes
    {
        /// <summary>
        /// Returns true if it saved the data (in other words if the domain has been unhandled)
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="domain"></param>
        /// <returns></returns>
        Boolean CheckOrSave(String endpoint, String domain, long position);
        
        DateTime? LastHeartbeat(String endpoint, String domain);
        long LastPosition(String endpoint, String domain);
        void Heartbeat(String endpoint, String domain, DateTime Timestamp, long position);
    }
}
