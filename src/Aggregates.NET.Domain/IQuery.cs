using Aggregates.Contracts;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public interface IQuery<TResponse> : IMessage where TResponse : IQueryResponse
    {
    }
}
