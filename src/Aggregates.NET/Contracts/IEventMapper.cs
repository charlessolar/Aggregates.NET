using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Contracts
{
    public interface IEventMapper
    {
        Type GetMappedTypeFor(Type type);
    }
}
