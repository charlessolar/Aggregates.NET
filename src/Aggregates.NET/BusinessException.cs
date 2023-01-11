using System;
using System.Runtime.Serialization;

namespace Aggregates
{
    [Serializable]
    public class BusinessException : Exception
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
        protected BusinessException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            Rule = (string)info.GetValue("Rule", typeof(string));
        }
        public override void GetObjectData(SerializationInfo info, StreamingContext ctx)
        {
            base.GetObjectData(info, ctx);
            info.AddValue("Rule", Rule);
        }
    }
}
