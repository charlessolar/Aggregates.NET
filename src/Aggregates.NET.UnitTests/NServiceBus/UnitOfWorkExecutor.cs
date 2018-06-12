using Aggregates.Contracts;
using Aggregates.Messages;
using FakeItEasy;
using FluentAssertions;
using NServiceBus.Pipeline;
using NServiceBus.Testing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.NServiceBus
{
    public class UnitOfWorkExecutor : TestSubject<Internal.UnitOfWorkExecutor>
    {
        [Fact]
        public Task ShouldCreateChildContainer()
        {
            return Task.CompletedTask;
        }
        [Fact]
        public Task ShouldNotCreateUnitOfWorkForNonSendMessages()
        {
            return Task.CompletedTask;
        }

        [Fact]
        public Task ShouldIgnoreAcceptOrRejectReplies()
        {
            return Task.CompletedTask;
        }
        [Fact]
        public Task ShouldRunBegin()
        {
            return Task.CompletedTask;
        }
        [Fact]
        public Task ShouldRunEnd()
        {
            return Task.CompletedTask;
        }
        [Fact]
        public Task ShouldRunEndWithException()
        {
            return Task.CompletedTask;
        }
        [Fact]
        public Task ShouldSaveAndReuseBags()
        {
            return Task.CompletedTask;
        }
        [Fact]
        public Task ShouldNotRequireIAppUnitOfWork()
        {
            return Task.CompletedTask;
        }
    }
}
