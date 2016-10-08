using System;

namespace Aggregates.Exceptions
{
    public class SubscriptionCanceled : Exception
    {
        public SubscriptionCanceled()
        { }
        public SubscriptionCanceled(string message) : base(message) { }
    }
}
