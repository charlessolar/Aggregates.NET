using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Aggregates
{
    [Serializable]
    public class BusinessException : System.Exception
    {
        public BusinessException() : base("Business rule failure") { }
        public BusinessException(string rule) : base($"Business rule [{rule}] failed")
        {
            Rule = rule;
        }
        public BusinessException(string rule, string message) : base($"Business rule [{rule}] failed: {message}")
        {
            Rule = rule;
        }

        public string Rule { get; private set; }
        // Constructor needed for serialization 
        // when exception propagates from a remote server to the client.
        protected BusinessException(SerializationInfo info,
            StreamingContext context) : base(info, context) { }
    }
}
