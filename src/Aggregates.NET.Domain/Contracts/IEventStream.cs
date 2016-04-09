using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IEventStream
    {
        String Bucket { get; }
        String StreamId { get; }
        Int32 StreamVersion { get; }
        Int32 CommitVersion { get; }

        IEnumerable<IWritableEvent> Events { get; }
        IEnumerable<IWritableEvent> Uncommitted { get; }

        void Add(Object @event, IDictionary<String, String> headers);
        void AddSnapshot(Object memento, IDictionary<String, String> headers);
        Task Commit(Guid commitId, IDictionary<String, String> commitHeaders);
        
        void AddChild(IEventStream stream);

        void ClearChanges();
        IEventStream Clone();
    }
}