using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.ValueObject
{
    [TestFixture]
    public class ValueObjectTests
    {
        private class Address : ValueObject<Address>
        {
            private readonly string _address1;
            private readonly string _city;
            private readonly string _state;

            public Address(string address1, string city, string state)
            {
                _address1 = address1;
                _city = city;
                _state = state;
            }

            public string Address1
            {
                get { return _address1; }
            }

            public string City
            {
                get { return _city; }
            }

            public string State
            {
                get { return _state; }
            }
        }

        private class ExpandedAddress : Address
        {
            private readonly string _address2;

            public ExpandedAddress(string address1, string address2, string city, string state)
                : base(address1, city, state)
            {
                _address2 = address2;
            }

            public string Address2
            {
                get { return _address2; }
            }

        }

        [Test]
        public void AddressEqualsWorksWithIdenticalAddresses()
        {
            Address address = new Address("Address1", "Austin", "TX");
            Address address2 = new Address("Address1", "Austin", "TX");

            Assert.IsTrue(address.Equals(address2));
        }

        [Test]
        public void AddressEqualsWorksWithNonIdenticalAddresses()
        {
            Address address = new Address("Address1", "Austin", "TX");
            Address address2 = new Address("Address2", "Austin", "TX");

            Assert.IsFalse(address.Equals(address2));
        }

        [Test]
        public void AddressEqualsWorksWithNulls()
        {
            Address address = new Address(null, "Austin", "TX");
            Address address2 = new Address("Address2", "Austin", "TX");

            Assert.IsFalse(address.Equals(address2));
        }

        [Test]
        public void AddressEqualsWorksWithNullsOnOtherObject()
        {
            Address address = new Address("Address2", "Austin", "TX");
            Address address2 = new Address("Address2", null, "TX");

            Assert.IsFalse(address.Equals(address2));
        }

        [Test]
        public void AddressEqualsIsReflexive()
        {
            Address address = new Address("Address1", "Austin", "TX");

            Assert.IsTrue(address.Equals(address));
        }

        [Test]
        public void AddressEqualsIsSymmetric()
        {
            Address address = new Address("Address1", "Austin", "TX");
            Address address2 = new Address("Address2", "Austin", "TX");

            Assert.IsFalse(address.Equals(address2));
            Assert.IsFalse(address2.Equals(address));
        }

        [Test]
        public void AddressEqualsIsTransitive()
        {
            Address address = new Address("Address1", "Austin", "TX");
            Address address2 = new Address("Address1", "Austin", "TX");
            Address address3 = new Address("Address1", "Austin", "TX");

            Assert.IsTrue(address.Equals(address2));
            Assert.IsTrue(address2.Equals(address3));
            Assert.IsTrue(address.Equals(address3));
        }

        [Test]
        public void AddressOperatorsWork()
        {
            Address address = new Address("Address1", "Austin", "TX");
            Address address2 = new Address("Address1", "Austin", "TX");
            Address address3 = new Address("Address2", "Austin", "TX");

            Assert.IsTrue(address == address2);
            Assert.IsTrue(address2 != address3);
        }

        [Test]
        public void DerivedTypesBehaveCorrectly()
        {
            Address address = new Address("Address1", "Austin", "TX");
            ExpandedAddress address2 = new ExpandedAddress("Address1", "Apt 123", "Austin", "TX");

            Assert.IsFalse(address.Equals(address2));
            Assert.IsFalse(address == address2);
        }

        [Test]
        public void EqualValueObjectsHaveSameHashCode()
        {
            Address address = new Address("Address1", "Austin", "TX");
            Address address2 = new Address("Address1", "Austin", "TX");

            Assert.AreEqual(address.GetHashCode(), address2.GetHashCode());
        }

        [Test]
        public void TransposedValuesGiveDifferentHashCodes()
        {
            Address address = new Address(null, "Austin", "TX");
            Address address2 = new Address("TX", "Austin", null);

            Assert.AreNotEqual(address.GetHashCode(), address2.GetHashCode());
        }

        [Test]
        public void UnequalValueObjectsHaveDifferentHashCodes()
        {
            Address address = new Address("Address1", "Austin", "TX");
            Address address2 = new Address("Address2", "Austin", "TX");

            Assert.AreNotEqual(address.GetHashCode(), address2.GetHashCode());
        }

        [Test]
        public void TransposedValuesOfFieldNamesGivesDifferentHashCodes()
        {
            Address address = new Address("_city", null, null);
            Address address2 = new Address(null, "_address1", null);

            Assert.AreNotEqual(address.GetHashCode(), address2.GetHashCode());
        }

        [Test]
        public void DerivedTypesHashCodesBehaveCorrectly()
        {
            ExpandedAddress address = new ExpandedAddress("Address99999", "Apt 123", "New Orleans", "LA");
            ExpandedAddress address2 = new ExpandedAddress("Address1", "Apt 123", "Austin", "TX");

            Assert.AreNotEqual(address.GetHashCode(), address2.GetHashCode());
        }

    }
}
