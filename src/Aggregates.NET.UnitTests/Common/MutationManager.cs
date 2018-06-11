using Aggregates.Contracts;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common
{
    public class MutationManager : Test
    {
        [Fact]
        public void ShouldAddMutation()
        {
            var mutator = Fake<IMutate>();
            Aggregates.MutationManager.RegisterMutator("test", mutator.GetType());
            Aggregates.MutationManager.Registered.Should().HaveCount(1);
        }
        [Fact]
        public void ShouldNotRegisterNonIMutate()
        {
            var e = Record.Exception(() => Aggregates.MutationManager.RegisterMutator("test", typeof(int)));
            e.Should().BeOfType<ArgumentException>();
        }
        [Fact]
        public void ShouldRemoveMutator()
        {
            var mutator = Fake<IMutate>();
            Aggregates.MutationManager.RegisterMutator("test", mutator.GetType());
            Aggregates.MutationManager.DeregisterMutator("test");
            Aggregates.MutationManager.Registered.Should().HaveCount(0);
        }
    }
}
