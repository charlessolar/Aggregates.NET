using Aggregates.Contracts;
using Aggregates.Exceptions;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common.Exceptions
{
    public class GenericExceptions : Test
    {
        [Fact]
        public void ShouldHaveEntityBucketIdAndParentsInConflictResolutionFailedException()
        {
            var e = new ConflictResolutionFailedException(typeof(FakeEntity), "testBucket", "testId", new Id[] { "testParent" });
            e.Message.Should().ContainAll(typeof(FakeEntity).Name, "testBucket", "testId", "testParent");
        }
        [Fact]
        public void ShouldHaveProjectionNameExistingAndDesiredDefinition()
        {
            var e = new EndpointVersionException("projectionName", "currentProjection", "desiredProjection");
            e.Message.Should().ContainAll("projectionName", "currentProjection", "desiredProjection");
        }
        [Fact]
        public void ShouldHaveEntityBucketIdAndParentsInEntityAlreadyExistsException()
        {
            var e = new EntityAlreadyExistsException<FakeEntity>("testBucket", "testId", new Id[] { "testParent " });
            e.Message.Should().ContainAll(typeof(FakeEntity).Name, "testBucket", "testId", "testParent");
        }
        [Fact]
        public void ShouldHaveStateAndHandler()
        {
            var e = new NoRouteException(typeof(FakeState), "testHandler");
            e.Message.Should().ContainAll(typeof(FakeState).Name, "testHandler");
        }
        [Fact]
        public void ShouldHaveStreamAndClient()
        {
            var e = new NotFoundException("testStream", new IPEndPoint(IPAddress.Any, 2020));
            e.Message.Should().ContainAll("testStream", IPAddress.Any.ToString());
        }
        [Fact]
        public void ShouldHaveMessageAndInnerExceptionInPersistenceException()
        {
            var e = new PersistenceException("testMessage", new Exception("testInner"));
            e.Message.Should().Be("testMessage");
            e.InnerException.Message.Should().Be("testInner");
        }
        [Fact]
        public void ShouldHaveMessageAndInnerExceptionInVersionException()
        {
            var e = new VersionException("testMessage", new Exception("testInner"));
            e.Message.Should().Be("testMessage");
            e.InnerException.Message.Should().Be("testInner");
        }
    }
}
