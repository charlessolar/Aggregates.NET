using Aggregates.Contracts;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common
{
    public class VersionRegistrar : TestSubject<Internal.VersionRegistrar>
    {
        public VersionRegistrar()
        {
            Internal.VersionRegistrar.Clear();
        }

        [Fact]
        public void LoadTypeIsLoaded()
        {
            Sut.Load(new[] { typeof(FakeDomainEvent.FakeEvent) });
            var named = Sut.GetVersionedName(typeof(FakeDomainEvent.FakeEvent));
            Sut.GetNamedType(named).Should().Be(typeof(FakeDomainEvent.FakeEvent));
        }

        [Fact]
        public void DuplicatedTypeIsFine()
        {
            Sut.Load(new[] { typeof(FakeDomainEvent.FakeEvent), typeof(FakeDomainEvent.FakeEvent) });
            var named = Sut.GetVersionedName(typeof(FakeDomainEvent.FakeEvent));
            Sut.GetNamedType(named).Should().Be(typeof(FakeDomainEvent.FakeEvent));
        }
        [Fact]
        public void NonVersionedWorks()
        {
            Sut.Load(new[] { typeof(FakeNotHandledEvent.UnknownEvent) });
            var named = Sut.GetVersionedName(typeof(FakeNotHandledEvent.UnknownEvent));
            Sut.GetNamedType(named).Should().Be(typeof(FakeNotHandledEvent.UnknownEvent));
        }
        [Fact]
        public void GetNonLoadedType()
        {
            Action act = () => Sut.GetVersionedName(typeof(FakeDomainEvent.FakeEvent));
            act.Should().NotThrow();
        }
        [Fact]
        public void GetNamedTypeWrongFormat()
        {
            Action act = () => Sut.GetNamedType("test");
            act.Should().Throw<ArgumentException>();
        }
        [Fact]
        public void GetNonesitantType()
        {
            Action act = () => Sut.GetNamedType("Test.Event v1");
            act.Should().Throw<InvalidOperationException>();
        }
        [Fact]
        public void GetTypeNoVersion()
        {
            Sut.Load(new[] { typeof(FakeDomainEvent.FakeEvent) });
            var named = Sut.GetVersionedName(typeof(FakeDomainEvent.FakeEvent));

            var withoutVersion = named.Substring(0, named.Length - 5);
            Sut.GetNamedType(withoutVersion).Should().Be(typeof(FakeDomainEvent.FakeEvent));
        }
        [Fact]
        public void GetTypeWrongVersion()
        {
            Sut.Load(new[] { typeof(FakeDomainEvent.FakeEvent) });
            var named = Sut.GetVersionedName(typeof(FakeDomainEvent.FakeEvent));

            var withoutVersion = named.Substring(0, named.Length - 5);
            Action act = () => Sut.GetNamedType($"{withoutVersion} v5");
            act.Should().Throw<InvalidOperationException>();
        }
        [Fact]
        public void GetTypeOldVersion()
        {
            Sut.Load(new[] { typeof(FakeDomainEvent.FakeEvent), typeof(FakeDomainEvent.FakeOldEvent) });
            var named = Sut.GetVersionedName(typeof(FakeDomainEvent.FakeEvent));

            var withoutVersion = named.Substring(0, named.Length - 5);
            Sut.GetNamedType($"{withoutVersion} v99").Should().Be(typeof(FakeDomainEvent.FakeOldEvent));
        }
    }
}
