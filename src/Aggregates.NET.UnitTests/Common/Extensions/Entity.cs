using Aggregates.Contracts;
using Aggregates.Extensions;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common.Extensions
{
    public class Entity : Test
    {
        [Fact]
        public void ShouldCreateParentsString()
        {
            var entity = Fake<IEntity>();
            A.CallTo(() => entity.Parents).Returns(new Id[] { "Parent1", "Parent2" });

            var parents = entity.BuildParentsString();
            parents.Should().ContainAll("Parent1", "Parent2");
        }
    }
}
