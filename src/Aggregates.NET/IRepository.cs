using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public interface IRepositoryBase : IDisposable
    {
        void Commit(Guid commitId, IDictionary<String, String> headers);
    }

    public interface IRepository<T> : IRepositoryBase where T : class, IEventSourceBase
    {
        T Get<T, TId>(TId id) where T : class, IEventSource<TId>;
        T Get<T, TId>(TId id, Int32 version) where T : class, IEventSource<TId>;
        T Get<T, TId>(String bucketId, TId id) where T : class, IEventSource<TId>;
        T Get<T, TId>(String bucketId, TId id, Int32 version) where T : class, IEventSource<TId>;

        T New<T, TId, TEvent>(String bucketId, TId id, Action<TEvent> action) where T : class, IEventSource<TId>;
        T New<T, TId, TEvent>(TId id, Action<TEvent> action) where T : class, IEventSource<TId>;

    }
}
