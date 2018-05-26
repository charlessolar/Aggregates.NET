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
            context.UoW.Test<Domain.World>().Plan("World").HasEvent<WorldCreated>(x =>
            {
            });

            var messageId = Guid.NewGuid();
            await handler.Handle(new SayHello
            {
                MessageId = messageId,
                Message = "test"
            }, context).ConfigureAwait(false);

            context.UoW.Test<Domain.World>().Check("World").Check<Domain.Message>(messageId).Raised<SaidHello>(x =>
            {
                x.MessageId = messageId;
                x.Message = "test";
            });
        }
        [Theory, AutoFakeItEasyData]
        public async Task should_create_world(
            TestableContext context,
            Handler handler
            )
        {

            var messageId = Guid.NewGuid();
            await handler.Handle(new SayHello
            {
                MessageId = messageId,
                Message = "test"
            }, context).ConfigureAwait(false);

            context.UoW.Test<Domain.World>().Check("World")
                .Raised<WorldCreated>(x => { })
                .Check<Domain.Message>(messageId)
                .Raised<SaidHello>(x =>
                {
                    x.MessageId = messageId;
                    x.Message = "test";
                });
        }
    }
}
