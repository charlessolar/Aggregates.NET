using Aggregates;
using AutoFixture;
using AutoFixture.AutoFakeItEasy;
using AutoFixture.Xunit2;
using Domain;
using Language;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Tests
{
    public class AutoFakeItEasyDataAttribute : AutoDataAttribute
    {
        public AutoFakeItEasyDataAttribute()
            : base(() => new Fixture().Customize(new AutoFakeItEasyCustomization()))
        {
        }
    }
    public class World
    {
        [Theory, AutoFakeItEasyData]
        public async Task should_send_two_echos(
            TestableContext context,
            Handler handler
            )
        {
            context.Extensions.Set("CommandDestination", "domain");

            var @event = context.Create<SaidHello>(x => { });

            await handler.Handle(@event, context).ConfigureAwait(false);

            Assert.Equal(2, context.SentMessages.Select(x => x.Message).OfType<Echo>().Count());             
        }
    }
}
