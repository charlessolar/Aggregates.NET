using Aggregates.Contracts;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class HostedService : IHostedService
    {
        readonly IConfiguration _config;
        readonly IServiceProvider _provider;

        public HostedService(IConfiguration config, IServiceProvider provider)
        {
            _config = config;
            _provider = provider;
        }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return _config.Start(_provider);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return _config.Stop(_provider);
        }
    }
}
