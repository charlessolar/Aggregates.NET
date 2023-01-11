using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common
{
    public class Settings : Test
    {
        [Fact]
        public async Task ShouldBuildDefaultConfiguration()
        {
            var container = Fake<IServiceCollection>();
            var config = await Aggregates.Configuration.Build(container, config =>
             {
             }).ConfigureAwait(false);

            config.Setup.Should().BeTrue();
        }


        [Fact]
        public async Task ShouldRequireContainerDefinition()
        {
            var container = Fake<IServiceCollection>();
            var e = await Record.ExceptionAsync(() => Aggregates.Configuration.Build(null, config => { })).ConfigureAwait(false);
            e.Should().BeOfType<ArgumentException>();
        }

        [Fact]
        public async Task ShouldCallRegistrationTasks()
        {
            bool called = false;
            var collection = Fake<IServiceCollection>();
            var provider = Fake<IServiceProvider>();
            await Aggregates.Configuration.Build(collection, config =>
            {
                Internal.Settings.RegistrationTasks.Add((container, _) =>
                {
                    called = true;
                    return Task.CompletedTask;
                });
            }).ConfigureAwait(false);

            called.Should().BeTrue();
        }
        [Fact]
        public async Task ShouldNotBeSetupAfterRegistrationException()
        {
            var collection = Fake<IServiceCollection>();
            var provider = Fake<IServiceProvider>();
            var e = await Record.ExceptionAsync(() => Aggregates.Configuration.Build(collection, config =>
            {
                Internal.Settings.RegistrationTasks.Add((container, _) =>
                {
                    throw new Exception();
                });
            })).ConfigureAwait(false);

            e.Should().BeOfType<Exception>();
        }
        [Fact]
        public async Task DoesNotThrowsWhenNoUnitOfWork()
        {
            var collection = Fake<IServiceCollection>();
            var provider = Fake<IServiceProvider>();
            A.CallTo(() => provider.GetService(typeof(Aggregates.UnitOfWork.IUnitOfWork))).Returns(null);

            var config = await Aggregates.Configuration.Build(collection, config =>
            {
            });

            await config.Start(provider).ConfigureAwait(false);

            config.Setup.Should().BeTrue();
        }
        [Fact]
        public async Task ThrowsWhenDoesntImplementBaseUnitOfWork()
        {
            var collection = Fake<IServiceCollection>();
            var provider = Fake<IServiceProvider>();
            A.CallTo(() => provider.GetService(typeof(Aggregates.UnitOfWork.IUnitOfWork))).Returns(Fake<Aggregates.UnitOfWork.IUnitOfWork>());

            var config = await Aggregates.Configuration.Build(collection, config =>
            {
            });

            var e = await Record.ExceptionAsync(() => config.Start(provider)).ConfigureAwait(false);

            e.Should().BeOfType<InvalidOperationException>();
            config.Setup.Should().BeFalse();
        }



        [Fact]
        public async Task ShouldSetOptions()
        {
            var provider = Fake<IServiceCollection>();
            var config = await Aggregates.Configuration.Build(provider, config =>
            {
                config.SetEndpointName("test");
                config.SetSlowAlertThreshold(TimeSpan.FromSeconds(1));
                config.SetExtraStats(true);
                config.SetStreamIdGenerator((type, streamType, bucket, stream, parents) => "test");
                config.SetReadSize(1);
                config.SetCompression(Compression.All);
                config.SetUniqueAddress("test");
                config.SetCommandDestination("test");
                config.ReceiveAllEvents();
                config.SetRetries(1);
                config.SetTrackChildren();
                config.SetDevelopmentMode();
            }).ConfigureAwait(false);

            config.Settings.Endpoint.Should().Be("test");
            config.Settings.SlowAlertThreshold.Should().Be(TimeSpan.FromSeconds(1));
            config.Settings.ExtraStats.Should().BeTrue();
            config.Settings.Generator(null, null, null, null, null).Should().Be("test");
            config.Settings.ReadSize.Should().Be(1);
            config.Settings.Compression.Should().Be(Compression.All);
            config.Settings.UniqueAddress.Should().Be("test");
            config.Settings.CommandDestination.Should().Be("test");
            config.Settings.AllEvents.Should().BeTrue();
            config.Settings.Retries.Should().Be(1);
            config.Settings.TrackChildren.Should().BeTrue();
            config.Settings.DevelopmentMode.Should().BeTrue();
        }

        [Fact]
        public async Task ShouldUseMetrics()
        {
            var collection = Fake<IServiceCollection>();
            var provider = Fake<IServiceProvider>();

            var config = await Aggregates.Configuration.Build(collection, config =>
            {
                config.AddMetrics<FakeMetrics>();
            });

            A.CallTo(() =>
                collection.Add(
                    A<ServiceDescriptor>
                        .That.Matches(x => x.ServiceType == typeof(Contracts.IMetrics) && x.ImplementationType == typeof(FakeMetrics)))).MustHaveHappened();
        }
        [Fact]
        public async Task UseApplicationUow()
        {
            var collection = Fake<IServiceCollection>();
            var provider = Fake<IServiceProvider>();

            var config = await Aggregates.Configuration.Build(collection, config =>
            {
                config.Application<FakeAppUnitOfWork>();
            });

            A.CallTo(() =>
                collection.Add(
                    A<ServiceDescriptor>
                        .That.Matches(x => x.ServiceType == typeof(Aggregates.UnitOfWork.IApplicationUnitOfWork) && x.ImplementationType == typeof(FakeAppUnitOfWork)))).MustHaveHappened();

        }
        [Fact]
        public async Task ProxyUowAppUnitOfWorks()
        {
            var collection = Fake<IServiceCollection>();
            var provider = Fake<IServiceProvider>();

            var config = await Aggregates.Configuration.Build(collection, config =>
            {
                config.Application<SimpleUnitOfWork, FakeAppUnitOfWork>();
            });

            A.CallTo(() =>
                collection.Add(
                    A<ServiceDescriptor>
                        .That.Matches(x => x.ServiceType == typeof(SimpleUnitOfWork) && x.ImplementationType == typeof(FakeAppUnitOfWork)))).MustHaveHappened();

        }
        [Fact]
        public async Task UseDomainUow()
        {
            var collection = Fake<IServiceCollection>();
            var provider = Fake<IServiceProvider>();

            var config = await Aggregates.Configuration.Build(collection, config =>
            {
                config.Domain();
            });

            A.CallTo(() =>
                collection.Add(
                    A<ServiceDescriptor>
                        .That.Matches(x => x.ServiceType == typeof(Aggregates.UnitOfWork.IDomainUnitOfWork) && x.ImplementationType == typeof(Internal.UnitOfWork)))).MustHaveHappened();

        }

    }
}
