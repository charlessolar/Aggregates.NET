using System;
using System.Linq.Expressions;
using Aggregates.Specifications;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Common.Specifications
{
    public class Returns : Specification<bool>
    {
        public Returns() { }
        public Returns(bool @return) { Return = @return; }

        public bool Return { get; set; }
        public override Expression<Func<bool, bool>> Predicate
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
        private Returns _false;
        private Returns _true;

        [SetUp]
        public void Setup()
        {
            _false = new Returns { Return = false };
            _true = new Returns { Return = true };
        }

        [Test]
        public void and_true()
        {
            var spec = _true & _true;
            Assert.True(spec.IsSatisfiedBy(true));
        }

        [Test]
        public void and_false()
        {
            var spec = _true & _false;
            Assert.False(spec.IsSatisfiedBy(true));
        }

        [Test]
        public void or_true()
        {
            var spec = _true | _false;
            Assert.True(spec.IsSatisfiedBy(true));
        }

        [Test]
        public void or_false()
        {
            var spec = _false | _false;
            Assert.False(spec.IsSatisfiedBy(true));
        }

        [Test]
        public void not_true()
        {
            var spec = !_false;
            Assert.True(spec.IsSatisfiedBy(true));
        }

        [Test]
        public void not_false()
        {
            var spec = !_true;
            Assert.False(spec.IsSatisfiedBy(true));
        }


        [Test]
        public void grouping_test()
        {
            var spec = (_true && _false) | _true;
            Assert.True(spec.IsSatisfiedBy(true));
        }
        [Test]
        public void grouping2_test()
        {
            var spec = !(_true && _false) | _false;
            Assert.True(spec.IsSatisfiedBy(true));
        }
    }
}
