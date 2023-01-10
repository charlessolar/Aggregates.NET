using Aggregates.Extensions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Exceptions
{
    public class EntityAlreadyExistsException : Exception
    {
        public EntityAlreadyExistsException(string entityType, string bucket, Id id, Id[] parents) :
            base($"New stream [{id}] bucket [{bucket}] parents [{parents.BuildParentsString()}] entity {entityType} already exists in store")
        { }
    }
}
