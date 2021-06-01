using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IConfiguration
    {
        bool Setup { get; }
        Configure Settings { get; }

        Task Start();
    }
}
