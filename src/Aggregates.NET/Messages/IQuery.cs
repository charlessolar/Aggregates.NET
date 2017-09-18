using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Messages
{
    public interface IQuery<TResponse> : IMessage
    {
    }
}
