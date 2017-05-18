using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IMessagePublisher
    {
        Task Publish<T>(string streamName, IEnumerable<IFullEvent> events, IDictionary<string, string> commitHeaders) where T : class, IEventSource;
    }
}
