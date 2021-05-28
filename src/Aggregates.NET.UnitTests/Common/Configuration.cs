using Aggregates.Contracts;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common
{
    public class Configuration : TestSubject<Aggregates.Configuration>
    {
        [Fact]
        public async Task ShouldBuildDefaultConfiguration()
        {
            await Sut.Build(config =>
            {
                config.Container = Fake<IContainer>();
            }).ConfigureAwait(false);

            Sut.Setup.Should().BeTrue();
        }


        [Fact]
        public async Task ShouldRequireContainerDefinition()
        {
            var e = await Record.ExceptionAsync(() => Sut.Build(config => { })).ConfigureAwait(false);
            e.Should().BeOfType<InvalidOperationException>();
        }

        [Fact]
        public async Task ShouldCallRegistrationTasks()
        {
            bool called = false;
            await Sut.Build(config =>
            {
                config.Container = Fake<IContainer>();
                config.RegistrationTasks.Add((_) =>
                {
                    called = true;
                    return Task.CompletedTask;
                });
            }).ConfigureAwait(false);

            called.Should().BeTrue();
        }
        [Fact]
        public async Task ShouldNotCallSetupTasks()
        {
            bool called = false;
            await Sut.Build(config =>
            {
                config.Container = Fake<IContainer>();
                config.SetupTasks.Add((_) =>
                {
                    called = true;
                    return Task.CompletedTask;
                });
            }).ConfigureAwait(false);

            called.Should().BeFalse();
        }
        [Fact]
        public async Task ShouldNotBeSetupAfterRegistrationException()
        {
            var e = await Record.ExceptionAsync(() => Sut.Build(config =>
            {
                config.Container = Fake<IContainer>();
                config.RegistrationTasks.Add((_) =>
                {
                    throw new Exception();
                });
            })).ConfigureAwait(false);

            e.Should().BeOfType<Exception>();
            Sut.Setup.Should().BeFalse();
        }
        [Fact]
        public async Task ShouldNotBeSetupAfterSetupException()
        {
            await Sut.Build(config =>
            {
                config.Container = Fake<IContainer>();
                config.SetupTasks.Add((_) =>
                {
                    throw new Exception();
                });
            });

            var e = await Record.ExceptionAsync(() => Sut.Start()).ConfigureAwait(false);

            e.Should().BeOfType<Exception>();
            Sut.Setup.Should().BeFalse();
        }
        [Fact]
        public async Task ShouldSetOptions()
        {
            await Sut.Build(config =>
            {
                config.Container = Fake<IContainer>();
                config.SetEndpointName("test");
                config.SetSlowAlertThreshold(TimeSpan.FromSeconds(1));
                config.SetExtraStats(true);
                config.SetStreamIdGenerator((type, streamType, bucket, stream, parents) => "test");
                config.SetReadSize(1);
                config.SetCompression(Compression.All);
                config.SetUniqueAddress("test");
                config.SetRetries(1);
                config.SetParallelMessages(1);
                config.SetParallelEvents(1);
                config.SetMaxConflictResolves(1);
                config.SetFlushSize(1);
                config.SetFlushInterval(TimeSpan.FromSeconds(1));
                config.SetDelayedExpiration(TimeSpan.FromSeconds(1));
                config.SetMaxDelayed(1);
                config.SetPassive();
            }).ConfigureAwait(false);

            Sut.Settings.Endpoint.Should().Be("test");
            Sut.Settings.SlowAlertThreshold.Should().Be(TimeSpan.FromSeconds(1));
            Sut.Settings.ExtraStats.Should().BeTrue();
            Sut.Settings.Generator(null, null, null, null, null).Should().Be("test");
            Sut.Settings.ReadSize.Should().Be(1);
            Sut.Settings.Compression.Should().Be(Compression.All);
            Sut.Settings.UniqueAddress.Should().Be("test");
            Sut.Settings.Retries.Should().Be(1);
            Sut.Settings.ParallelMessages.Should().Be(1);
            Sut.Settings.ParallelEvents.Should().Be(1);
            Sut.Settings.MaxConflictResolves.Should().Be(1);
            Sut.Settings.FlushSize.Should().Be(1);
            Sut.Settings.FlushInterval.Should().Be(TimeSpan.FromSeconds(1));
            Sut.Settings.DelayedExpiration.Should().Be(TimeSpan.FromSeconds(1));
            Sut.Settings.MaxDelayed.Should().Be(1);
            Sut.Settings.Passive.Should().BeTrue();
        }

    }
}
