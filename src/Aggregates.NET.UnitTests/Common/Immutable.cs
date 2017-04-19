using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Common
{
    [TestFixture]
    public class Immutable
    {
        struct test_struct
        {
            public int foo;
        }

        [Test]
        public void can_create_immutable()
        {
            var a = new Immutable<int>(5);
            var b = new Immutable<test_struct>(new test_struct {foo=0});
        }

        [Test]
        public void has_value()
        {
            var a = new Immutable<int>(5);
            var b = new Immutable<test_struct>(new test_struct {foo=1});
            Assert.True(a.HasValue);
            Assert.True(b.HasValue);
        }

        [Test]
        public void doesnt_have_value()
        {
            var a = new Immutable<int>();
            var b = new Immutable<test_struct>();
            Assert.False(a.HasValue);
            Assert.False(b.HasValue);
        }

        [Test]
        public void equals()
        {
            var test_struct = new test_struct() {foo=1};

            var a = new Immutable<int>(5);
            var b = new Immutable<test_struct>(test_struct);
            Assert.True(a.Equals(5));
            Assert.True(5 == a);
            Assert.True(a == 5);
            Assert.True(b.Equals(test_struct));
            Assert.True(test_struct == b);
            Assert.True(b == test_struct);

            var a2 = new Immutable<int>(5);
            var b2 = new Immutable<test_struct>(test_struct);
            Assert.True(a.Equals(a2));
            Assert.True(a == a2);
            Assert.True(b.Equals(b2));
            Assert.True(b2 == b);
            Assert.True(b == b2);

        }

        [Test]
        public void not_equals()
        {
            var test_struct = new test_struct() { foo = 1 };

            var a = new Immutable<int>(5);
            var b = new Immutable<test_struct>(test_struct);
            Assert.True(!a.Equals(6));
            Assert.True(6 != a);
            Assert.True(a != 6);

            test_struct.foo = 3;
            Assert.True(!b.Equals(test_struct));
            Assert.True(test_struct != b);
            Assert.True(b != test_struct);
            

            var test_struct2 = new test_struct() { foo = 2 };
            var a2 = new Immutable<int>(6);
            var b2 = new Immutable<test_struct>(test_struct2);
            Assert.True(!a.Equals(a2));
            Assert.True(a != a2);
        }

        [Test]
        public void to_string()
        {
            var a = new Immutable<int>(5);
            Assert.AreEqual("5", a.ToString());
        }

        [Test]
        public void hash_code()
        {
            var a = new Immutable<int>(5);
            Assert.AreEqual(5.GetHashCode(), a.GetHashCode());
        }

        [Test]
        public void changes_not_equal()
        {
            var a = new Immutable<int>(5);
            var a2 = a;

            Assert.True(ReferenceEquals(a,a2));

            a = 6;

            Assert.False(ReferenceEquals(a, a2));
            Assert.False(a == a2);
        }
    }
}
