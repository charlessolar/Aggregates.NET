using System;

namespace Aggregates.Exceptions
{
    public class ExistException : Exception
    {
        public ExistException(Type type, string bucket, Id id) : base($"No entity [{type.FullName}] in bucket [{bucket}] id [{id}] exists") { }
    }
}
