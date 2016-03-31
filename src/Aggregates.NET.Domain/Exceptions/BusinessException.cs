using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    [Serializable]
    public class BusinessException : Exception
    {
        public BusinessException() { }
        public BusinessException(String message) : base(message) { }
        public BusinessException(String message, Exception innerException) : base(message, innerException) { }

        // Constructor needed for serialization 
        // when exception propagates from a remoting server to the client.
        protected BusinessException(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
}
