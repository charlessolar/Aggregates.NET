using Aggregates.Specifications;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.Specifications
{
    public class Returns : Specification<Boolean>
    {
        public Returns() { }
        public Returns(Boolean @return) { Return = @return; }

        public Boolean Return { get; set; }
        public override Expression<Func<Boolean, Boolean>> Predicate
        {
            get
            {
                return a => Return;
            }
        }
    }

    [TestFixture]
    public class Specifications
    {
        private Returns False;
        private Returns True;

        [SetUp]
        public void Setup()
        {
            False = new Returns { Return = false };
            True = new Returns { Return = true };
        }

        [Test]
        public void and_true()
        {
            var spec = True & True;
            Assert.True(spec.IsSatisfiedBy(true));
        }

        [Test]
        public void and_false()
        {
            var spec = True & False;
            Assert.False(spec.IsSatisfiedBy(true));
        }

        [Test]
        public void or_true()
        {
            var spec = True | False;
            Assert.True(spec.IsSatisfiedBy(true));
        }

        [Test]
        public void or_false()
        {
            var spec = False | False;
            Assert.False(spec.IsSatisfiedBy(true));
        }

        [Test]
        public void not_true()
        {
            var spec = !False;
            Assert.True(spec.IsSatisfiedBy(true));
        }

        [Test]
        public void not_false()
        {
            var spec = !True;
            Assert.False(spec.IsSatisfiedBy(true));
        }


        [Test]
        public void grouping_test()
        {
            var spec = (True && False) | True;
            Assert.True(spec.IsSatisfiedBy(true));
        }
        [Test]
        public void grouping2_test()
        {
            var spec = !(True && False) | False;
            Assert.True(spec.IsSatisfiedBy(true));
        }
    }
}
