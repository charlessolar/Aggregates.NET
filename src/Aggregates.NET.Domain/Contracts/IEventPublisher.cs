using System.Collections.Generic;

namespace Aggregates.Contracts
{
    public interface IEventPublisher
    {
        void Publish(IEnumerable<object> events);
    }
}