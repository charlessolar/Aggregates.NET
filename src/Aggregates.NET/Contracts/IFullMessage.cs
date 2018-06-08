using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Contracts
{
    public interface IFullMessage
    {
        IMessage Message { get; }
        IDictionary<string,string> Headers { get; }
    }
}
