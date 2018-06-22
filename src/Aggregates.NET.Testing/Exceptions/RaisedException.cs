using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Aggregates.Exceptions
{
    public class RaisedException : Exception
    {
        public RaisedException(Messages.IEvent[] raisedEvents)
        {
            var raised = raisedEvents.Any() ? raisedEvents.Select(x => JsonConvert.SerializeObject(x)).Aggregate((cur, next) => $"{cur}\n{next}") : "No raised events";

            _message = $"Expected no change.\nRaised Events:\n{raised}";
        }

        private readonly string _message;
        public override string Message => _message;
    }
}
