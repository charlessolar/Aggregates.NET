using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Aggregates
{
    [Serializable]
    public class BusinessException : System.AggregateException
    {
        public BusinessException() { }
        public BusinessException(string message) : base(message) { }
        public BusinessException(string message, BusinessException innerException) : base(message, innerException) { }
        public BusinessException(string message, IEnumerable<BusinessException> innerExceptions) : base(message, innerExceptions) { }

        // Constructor needed for serialization 
        // when exception propagates from a remote server to the client.
        protected BusinessException(SerializationInfo info,
            StreamingContext context) : base(info, context) { }
    }
}
