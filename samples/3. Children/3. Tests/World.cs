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
        public async Task should_say_hello(
            TestableContext context,
            Handler handler
            )
        {
            context.UoW.Plan<Domain.World>("World").HasEvent<WorldCreated>(x =>
            {
            });

            await handler.Handle(new SayHello
            {
                MessageId = context.Id(),
                Message = "test"
            }, context).ConfigureAwait(false);

            context.UoW.Check<Domain.World>("World").Check<Domain.Message>(context.Id())
                .Raised<SaidHello>(x => x.Message == "test")
                .Raised<SaidHello>(x =>
                {
                    x.MessageId = context.Id();
                    x.Message = "test";
                });
        }
        [Theory, AutoFakeItEasyData]
        public async Task should_create_world(
            TestableContext context,
            Handler handler
            )
        {

            await handler.Handle(new SayHello
            {
                MessageId = context.Id(1),
                Message = "test"
            }, context).ConfigureAwait(false);

            context.UoW.Check<Domain.World>("World")
                .Raised<WorldCreated>(x => { })
                .Check<Domain.Message>(context.Id(1))
                .Raised<SaidHello>(x =>
                {
                    x.MessageId = context.Id(1);
                    x.Message = "test";
                });
        }
    }
}
