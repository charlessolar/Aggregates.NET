using Aggregates.Contracts;
using Aggregates.Internal;
using AutoFixture;
using AutoFixture.AutoFakeItEasy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Aggregates
{
    public abstract class Test
    {
        protected IFixture Fixture { get; private set; }

        public Test()
        {
            Fixture = new Fixture().Customize(new AutoFakeItEasyCustomization());

            Fixture.Customize<Id>(x => x.FromFactory(() => Guid.NewGuid()));
            Fixture.Customize<Fakes>(x => x.FromFactory(() =>
            {
                var factory = EntityFactory.For<Fakes>();

                var entity = factory.Create(Fake<string>(), Fake<Id>(), Fake<Id[]>());
                
                (entity as INeedDomainUow).Uow = Fake<IDomainUnitOfWork>();
                (entity as INeedEventFactory).EventFactory = Fake<IEventFactory>();
                (entity as INeedStore).Store = Fake<IStoreEvents>();
                (entity as INeedStore).OobWriter = Fake<IOobWriter>();

                return entity;
            }));
        }

        protected T Fake<T>() => Fixture.Create<T>();
        protected T[] Many<T>(int count = 3) => Fixture.CreateMany<T>(count).ToArray();
        protected T Inject<T>(T instance) where T : class
        {
            Fixture.Inject(instance);
            return instance;
        }
    }
}
