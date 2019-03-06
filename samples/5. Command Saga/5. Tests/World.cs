using Aggregates;
using AutoFixture;
using AutoFixture.AutoFakeItEasy;
using AutoFixture.Xunit2;
using Domain;
using Language;
using System;
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
        public async Task should_send_two_hellos_after_first(
            TestableContext context,
            Handler handler
            )
        {
            context.UoW.Plan<Domain.World>("World").HasEvent<SaidHello>(x =>
            {
                x.Message = "foo";
            });

            await handler.Handle(new SayHello
            {
                Message = "test"
            }, context).ConfigureAwait(false);

            context.UoW.Check<Domain.World>("World").Raised<SaidHello>(x =>
            {
                x.Message = "test";
            });
        }
    }
}
