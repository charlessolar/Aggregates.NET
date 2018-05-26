using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Aggregates.Exceptions
{
    public class DidNotRaisedException : Exception
    {
        public DidNotRaisedException(Messages.IEvent expectedEvent, Messages.IEvent[] raisedEvents)
        {
            var raised = raisedEvents.Any() ? raisedEvents.Select(x => JsonConvert.SerializeObject(x)).Aggregate((cur, next) => $"{cur}\n{next}") : "No raised events";

            _message = $"Expected raise: {JsonConvert.SerializeObject(expectedEvent)} did not occur.\nRaised Events:\n{raised}";
        }

        private readonly string _message;
        public override string Message => _message;
    }
}
