using System;

namespace Aggregates.Exceptions
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
