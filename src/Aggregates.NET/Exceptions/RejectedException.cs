using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    [Serializable]
    public class RejectedException :Exception
    {
        public RejectedException(Exception innerException) : base("REJECTION", innerException) { }

        // Constructor needed for serialization 
        // when exception propagates from a remoting server to the client.
        protected RejectedException(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
}
