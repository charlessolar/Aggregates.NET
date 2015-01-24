using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.ValueObject
{
    [TestFixture]
    public class SingleValueObjectTests
    {
        [Test]
        public void AddressEqualsWorksWithIdenticalAddresses()
        {
            var address = new SingleValueObject<String>("test");
            var address2 = new SingleValueObject<String>("test");

            Assert.IsTrue(address.Equals(address2));
        }

        [Test]
        public void AddressEqualsWorksWithNonIdenticalAddresses()
        {
            var address = new SingleValueObject<String>("test");
            var address2 = new SingleValueObject<String>("test2");

            Assert.IsFalse(address.Equals(address2));
        }

        [Test]
        public void AddressEqualsWorksWithNulls()
        {
            var address = new SingleValueObject<String>(null);
            var address2 = new SingleValueObject<String>("test");

            Assert.IsFalse(address.Equals(address2));
        }

        [Test]
        public void AddressEqualsIsReflexive()
        {
            var address = new SingleValueObject<String>("test");

            Assert.IsTrue(address.Equals(address));
        }

        [Test]
        public void AddressEqualsIsSymmetric()
        {
            var address = new SingleValueObject<String>("test");
            var address2 = new SingleValueObject<String>("test2");

            Assert.IsFalse(address.Equals(address2));
            Assert.IsFalse(address2.Equals(address));
        }

        [Test]
        public void AddressEqualsIsTransitive()
        {
            var address = new SingleValueObject<String>("test");
            var address2 = new SingleValueObject<String>("test");
            var address3 = new SingleValueObject<String>("test");

            Assert.IsTrue(address.Equals(address2));
            Assert.IsTrue(address2.Equals(address3));
            Assert.IsTrue(address.Equals(address3));
        }

        [Test]
        public void AddressOperatorsWork()
        {
            var address = new SingleValueObject<String>("test");
            var address2 = new SingleValueObject<String>("test");
            var address3 = new SingleValueObject<String>("test2");

            Assert.IsTrue(address == address2);
            Assert.IsTrue(address2 != address3);
        }

        [Test]
        public void EqualValueObjectsHaveSameHashCode()
        {
            var address = new SingleValueObject<String>("test");
            var address2 = new SingleValueObject<String>("test");

            Assert.AreEqual(address.GetHashCode(), address2.GetHashCode());
        }

        [Test]
        public void TransposedValuesGiveDifferentHashCodes()
        {
            var address = new SingleValueObject<String>("test");
            var address2 = new SingleValueObject<String>(null);

            Assert.AreNotEqual(address.GetHashCode(), address2.GetHashCode());
        }

        [Test]
        public void UnequalValueObjectsHaveDifferentHashCodes()
        {
            var address = new SingleValueObject<String>("test");
            var address2 = new SingleValueObject<String>("test2");

            Assert.AreNotEqual(address.GetHashCode(), address2.GetHashCode());
        }
    }
}