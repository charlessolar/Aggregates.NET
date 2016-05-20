using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    [Serializable]
    public class BusinessException : System.AggregateException
    {
        public BusinessException() { }
        public BusinessException(String message) : base(message) { }
        public BusinessException(String message, BusinessException innerException) : base(message, innerException) { }
        public BusinessException(String message, IEnumerable<BusinessException> innerExceptions) : base(message, innerExceptions) { }

        // Constructor needed for serialization 
        // when exception propagates from a remote server to the client.
        protected BusinessException(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
}
