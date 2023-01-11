using System;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IConfiguration
    {
        IServiceProvider ServiceProvider { get; }

        bool Setup { get; }
        ISettings Settings { get; }

        Task Start(IServiceProvider serviceProvider);
        Task Stop(IServiceProvider serviceProvider);
    }
}
