using Aggregates.Contracts;
using FakeItEasy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common
{
    public class HostedService : TestSubject<Aggregates.Internal.HostedService>
    {
        [Fact]
        public async Task CallsStart() {
            var config = Fake<IConfiguration>();
            await Sut.StartAsync(CancellationToken.None);

            A.CallTo(() => config.Start(A<IServiceProvider>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task CallsStop()
        {
            var config = Fake<IConfiguration>();
            await Sut.StopAsync(CancellationToken.None);

            A.CallTo(() => config.Stop(A<IServiceProvider>.Ignored)).MustHaveHappened();
        }
    }
}
