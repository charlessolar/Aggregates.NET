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
            await Aggregates.Configuration.Build(config =>
            {
                config.Container = Fake<IContainer>();
            }).ConfigureAwait(false);

            Aggregates.Configuration.Setup.Should().BeTrue();
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
            Aggregates.Configuration.Setup.Should().BeFalse();
        }
        [Fact]
        public async Task ShouldNotBeSetupAfterSetupException()
        {
            await Aggregates.Configuration.Build(config =>
            {
                config.Container = Fake<IContainer>();
                config.SetupTasks.Add((_) =>
                {
                    throw new Exception();
                });
            });

            var e = await Record.ExceptionAsync(() => Aggregates.Configuration.Start()).ConfigureAwait(false);

            e.Should().BeOfType<Exception>();
            Aggregates.Configuration.Setup.Should().BeFalse();
        }
        //[Fact]
        //public async Task ShouldSetOptions()
        //{
        //    await Aggregates.Configuration.Build(config =>
        //    {
        //        config.Container = Fake<IContainer>();
        //        config.SetEndpointName("test");
        //        config.SetSlowAlertThreshold(TimeSpan.FromSeconds(1));
        //        config.SetExtraStats(true);
        //        config.SetStreamIdGenerator((type, streamType, bucket, stream, parents) => "test");
        //        config.SetReadSize(1);
        //        config.SetCompression(Compression.All);
        //        config.SetUniqueAddress("test");
        //        config.SetRetries(1);
        //        config.SetParallelMessages(1);
        //        config.SetParallelEvents(1);
        //        config.SetMaxConflictResolves(1);
        //        config.SetFlushSize(1);
        //        config.SetFlushInterval(TimeSpan.FromSeconds(1));
        //        config.SetDelayedExpiration(TimeSpan.FromSeconds(1));
        //        config.SetMaxDelayed(1);
        //        config.SetPassive();
        //    }).ConfigureAwait(false);

        //    Aggregates.Configuration.Settings.Endpoint.Should().Be("test");
        //    Aggregates.Configuration.Settings.SlowAlertThreshold.Should().Be(TimeSpan.FromSeconds(1));
        //    Aggregates.Configuration.Settings.ExtraStats.Should().BeTrue();
        //    Aggregates.Configuration.Settings.Generator(null, null, null, null, null).Should().Be("test");
        //    Aggregates.Configuration.Settings.ReadSize.Should().Be(1);
        //    Aggregates.Configuration.Settings.Compression.Should().Be(Compression.All);
        //    Aggregates.Configuration.Settings.UniqueAddress.Should().Be("test");
        //    Aggregates.Configuration.Settings.Retries.Should().Be(1);
        //    Aggregates.Configuration.Settings.ParallelMessages.Should().Be(1);
        //    Aggregates.Configuration.Settings.ParallelEvents.Should().Be(1);
        //    Aggregates.Configuration.Settings.MaxConflictResolves.Should().Be(1);
        //    Aggregates.Configuration.Settings.FlushSize.Should().Be(1);
        //    Aggregates.Configuration.Settings.FlushInterval.Should().Be(TimeSpan.FromSeconds(1));
        //    Aggregates.Configuration.Settings.DelayedExpiration.Should().Be(TimeSpan.FromSeconds(1));
        //    Aggregates.Configuration.Settings.MaxDelayed.Should().Be(1);
        //    Aggregates.Configuration.Settings.Passive.Should().BeTrue();
        //}

    }
}
