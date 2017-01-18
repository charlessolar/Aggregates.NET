using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using NServiceBus;

namespace Aggregates.Internal
{
    class Mutating : IMutating
    {
        public Mutating(object message, IDictionary<string, string> headers)
        {
            this.Message = message;
            this.Headers = new Dictionary<string, string>(headers.ToDictionary(x => x.Key, x => x.Value));
        }

        public object Message { get; set; }
        public IDictionary<string, string> Headers { get; private set; }
    }
}
