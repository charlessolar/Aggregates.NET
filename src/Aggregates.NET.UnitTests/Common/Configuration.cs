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
    public class Configuration : Test
    {
        [Fact]
        public async Task ShouldBuildDefaultConfiguration()
        {
            var config = await Aggregates.Configuration.Build(config =>
             {
                 config.Container = Fake<IContainer>();
             }).ConfigureAwait(false);

            config.Setup.Should().BeTrue();
        }


        [Fact]
        public async Task ShouldRequireContainerDefinition()
        {
            var e = await Record.ExceptionAsync(() => Aggregates.Configuration.Build(config => { })).ConfigureAwait(false);
            e.Should().BeOfType<InvalidOperationException>();
        }

        [Fact]
        public async Task ShouldCallRegistrationTasks()
        {
            bool called = false;
            await Aggregates.Configuration.Build(config =>
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
            await Aggregates.Configuration.Build(config =>
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
            var e = await Record.ExceptionAsync(() => Aggregates.Configuration.Build(config =>
            {
                config.Container = Fake<IContainer>();
                config.RegistrationTasks.Add((_) =>
                {
                    throw new Exception();
                });
            })).ConfigureAwait(false);

            e.Should().BeOfType<Exception>();
        }
        [Fact]
        public async Task ShouldNotBeSetupAfterSetupException()
        {
            var config = await Aggregates.Configuration.Build(config =>
            {
                config.Container = Fake<IContainer>();
                config.SetupTasks.Add((_) =>
                {
                    throw new Exception();
                });
            });

            var e = await Record.ExceptionAsync(() => config.Start()).ConfigureAwait(false);

            e.Should().BeOfType<Exception>();
            config.Setup.Should().BeFalse();
        }
        [Fact]
        public async Task ShouldSetOptions()
        {
            var config = await Aggregates.Configuration.Build(config =>
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

            config.Settings.Endpoint.Should().Be("test");
            config.Settings.SlowAlertThreshold.Should().Be(TimeSpan.FromSeconds(1));
            config.Settings.ExtraStats.Should().BeTrue();
            config.Settings.Generator(null, null, null, null, null).Should().Be("test");
            config.Settings.ReadSize.Should().Be(1);
            config.Settings.Compression.Should().Be(Compression.All);
            config.Settings.UniqueAddress.Should().Be("test");
            config.Settings.Retries.Should().Be(1);
            config.Settings.ParallelMessages.Should().Be(1);
            config.Settings.ParallelEvents.Should().Be(1);
            config.Settings.MaxConflictResolves.Should().Be(1);
            config.Settings.FlushSize.Should().Be(1);
            config.Settings.FlushInterval.Should().Be(TimeSpan.FromSeconds(1));
            config.Settings.DelayedExpiration.Should().Be(TimeSpan.FromSeconds(1));
            config.Settings.MaxDelayed.Should().Be(1);
            config.Settings.Passive.Should().BeTrue();
        }

    }
}
