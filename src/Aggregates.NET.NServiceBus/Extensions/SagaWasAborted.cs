using System;

namespace Aggregates.Extensions
{
    public class SagaWasAborted : Exception
    {
        public Messages.IMessage Originating { get; private set; }
        public SagaWasAborted(Messages.IMessage original)
        {
            Originating = original;
        }
    }
}
