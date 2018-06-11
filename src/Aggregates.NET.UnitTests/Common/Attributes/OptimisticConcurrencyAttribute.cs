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
    public class OptimisticConcurrencyAttribute : Test
    {
        class FakeCustomResolver : IResolveConflicts
        {
            Task IResolveConflicts.Resolve<TEntity, TState>(TEntity entity, Guid commitId, IDictionary<string, string> commitHeaders)
            {
                throw new NotImplementedException();
            }
        }

        [Fact]
        public void ShouldCreateAttribute()
        {
            var attr = new Attributes.OptimisticConcurrencyAttribute(ConcurrencyConflict.Ignore);
        }
        [Fact]
        public void ShouldCreateCustomResolver()
        {
            var attr = new Attributes.OptimisticConcurrencyAttribute(ConcurrencyConflict.Custom, resolver: typeof(FakeCustomResolver));
        }
        [Fact]
        public void ShouldRequireCustomResolverToBeIResolveConflicts()
        {
            var e = Record.Exception(() => new Attributes.OptimisticConcurrencyAttribute(ConcurrencyConflict.Custom, resolver: typeof(int)));
            e.Should().BeOfType<ArgumentException>();
        }
        [Fact]
        public void ShouldRequireCustomResolverWhenCustom()
        {
            var e = Record.Exception(() => new Attributes.OptimisticConcurrencyAttribute(ConcurrencyConflict.Custom));
            e.Should().BeOfType<ArgumentException>();
        }
    }
}
