using Aggregates.Contracts;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
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
