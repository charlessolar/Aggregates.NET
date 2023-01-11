using Newtonsoft.Json;
using System;
using System.Linq;

namespace Aggregates.Exceptions
{
    public class NoMatchingEventException : Exception
    {
        public NoMatchingEventException(Messages.IEvent[] raisedEvents)
        {
            var raised = raisedEvents.Any() ? raisedEvents.Select(x => JsonConvert.SerializeObject(x)).Aggregate((cur, next) => $"{cur}\n{next}") : "No raised events";

            _message = $"No matching event found.\nRaised Events:\n{raised}";
        }

        private readonly string _message;
        public override string Message => _message;
    }

}
