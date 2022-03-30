using Aggregates.Contracts;
using Aggregates.Internal;
using Aggregates.Messages;
using AutoFixture;
using AutoFixture.AutoFakeItEasy;
using FakeItEasy;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Aggregates
{

    public abstract class Test
    {
        protected IFixture Fixture { get; private set; }
        protected ISettings Settings { get; private set; }
        protected IServiceProvider Provider { get; private set; }
        
        public Test()
        {
            Fixture = new Fixture().Customize(new AutoFakeItEasyCustomization { ConfigureMembers = true });


            Provider = Fake<IServiceProvider>();
            Fixture.Customize<Id>(x => x.FromFactory(() => Guid.NewGuid()));
            Fixture.Customize<IEvent>(x => x.FromFactory(() => new FakeDomainEvent.FakeEvent()));
            Fixture.Customize<FakeEntity>(x => x.FromFactory(() =>
            {
                var factory = EntityFactory.For<FakeEntity>();

                var entity = factory.Create(Fake<ILogger>(), Defaults.Bucket, Fake<Id>(), null, Many<FakeDomainEvent.FakeEvent>());

                (entity as INeedDomainUow).Uow = Fake<UnitOfWork.IDomainUnitOfWork>();
                (entity as INeedEventFactory).EventFactory = Fake<IEventFactory>();
                (entity as INeedStore).Store = Fake<IStoreEvents>();

                return entity;
            }));
            Fixture.Customize<FakeChildEntity>(x => x.FromFactory(() =>
            {
                var factory = EntityFactory.For<FakeChildEntity>();

                var entity = factory.Create(Fake<ILogger>(), Defaults.Bucket, Fake<Id>(), null, Many<FakeDomainEvent.FakeEvent>());

                (entity as INeedDomainUow).Uow = Fake<UnitOfWork.IDomainUnitOfWork>();
                (entity as INeedEventFactory).EventFactory = Fake<IEventFactory>();
                (entity as INeedStore).Store = Fake<IStoreEvents>();

                entity.Parent = Fake<FakeEntity>();
                return entity;
            }));
        }

        protected T Fake<T>(bool inject = true)
        {
            var instance = Fixture.Create<T>();
            if(inject)
                Inject(instance);
            return instance;
        }
        protected T[] Many<T>(int count = 3) => Fixture.CreateMany<T>(count).ToArray();
        protected void Inject<T>(T instance) => Fixture.Inject(instance);
    }
}
