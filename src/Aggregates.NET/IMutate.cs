using Aggregates.Contracts;

namespace Aggregates
{
    public interface IMutate
    {
        IMutating MutateIncoming(IMutating mutating);
        IMutating MutateOutgoing(IMutating mutating);
    }
}
