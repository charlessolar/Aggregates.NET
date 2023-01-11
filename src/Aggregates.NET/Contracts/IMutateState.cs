using Aggregates.Messages;

namespace Aggregates.Contracts
{
    interface IMutateState
    {
        void Handle(IState state, IEvent @event);
    }
}
