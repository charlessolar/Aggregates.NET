using System;

namespace Aggregates.Exceptions
{
    public class SagaAbortionFailureException : Exception
    {
        public Messages.IMessage Originating { get; private set; }

        public SagaAbortionFailureException(Messages.IMessage originating) :
            base("Failed to run abort commands for saga")
        {
            Originating = originating;
        }
    }
}
