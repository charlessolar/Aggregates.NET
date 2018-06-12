using Aggregates.Contracts;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common.Extensions
{
    public class Dynamic : Test
    {
        [Fact]
        public void ShouldContainKey()
        {
            dynamic bag = new System.Dynamic.ExpandoObject();
            bag.Test = true;

            bool contains = Aggregates.Dynamic.ContainsProperty(bag, "Test");
            contains.Should().BeTrue();
        }
        [Fact]
        public void ShouldNotContainKey()
        {
            dynamic bag = new System.Dynamic.ExpandoObject();

            bool contains = Aggregates.Dynamic.ContainsProperty(bag, "Test");
            contains.Should().BeFalse();
        }
    }
}
