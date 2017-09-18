using System;
using NUnit.Framework;
using Aggregates.Internal;

namespace Aggregates.UnitTests.Common
{
    [TestFixture]
    public class Enumeration
    {
        private class IntEnum : Enumeration<IntEnum>
        {
            public static readonly IntEnum One = new IntEnum(1, "One");

            private IntEnum(int value, string displayName) : base(value, displayName) { }
        }
        private class StringEnum : Enumeration<StringEnum, string>
        {
            public static readonly StringEnum Test = new StringEnum("test", "Test");
            public static readonly StringEnum Two = new StringEnum("two", "Two");

            public StringEnum(string value, string displayName) : base(value, displayName) { }
        }


        [SetUp]
        public void Setup() { }

        [Test]
        public void int_enumeration()
        {
            var @enum = IntEnum.FromInt32(1);
            Assert.True(@enum.Equals(IntEnum.One));
            Assert.True(@enum == IntEnum.One);
            Assert.AreEqual(@enum.CompareTo(IntEnum.One), 0);
        }
        [Test]
        public void int_try_enumeration()
        {
            IntEnum @enum;
            IntEnum.TryFromInt32(1, out @enum);

            Assert.True(@enum.Equals(IntEnum.One));
            Assert.True(@enum == IntEnum.One);
            Assert.AreEqual(@enum.CompareTo(IntEnum.One), 0);
        }
        [Test]
        public void null_value_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new StringEnum(null, "test"));
        }
        [Test]
        public void to_string()
        {
            Assert.AreEqual(StringEnum.Test.ToString(), "Test");
        }
        [Test]
        public void object_equals()
        {
            Assert.False(StringEnum.Test.Equals(new object()));
            Assert.True(StringEnum.Test.Equals((object)StringEnum.Test));
            Assert.False(StringEnum.Test.Equals((object)StringEnum.Two));
        }
        [Test]
        public void get_hash_code()
        {
            Assert.AreEqual(StringEnum.Test.GetHashCode(), "test".GetHashCode());
            Assert.AreNotEqual(StringEnum.Test.GetHashCode(), "false".GetHashCode());
        }
        [Test]
        public void has_value()
        {
            Assert.True(StringEnum.HasValue("test"));
            Assert.False(StringEnum.HasValue("one"));
        }
        [Test]
        public void has_display_name()
        {
            Assert.True(StringEnum.HasDisplayName("Test"));
            Assert.False(StringEnum.HasDisplayName("One"));
        }
        [Test]
        public void from_value()
        {
            Assert.Throws<ArgumentException>(() => StringEnum.FromValue("false"));
            Assert.DoesNotThrow(() => StringEnum.FromValue("test"));
        }
        [Test]
        public void from_display_name()
        {
            Assert.Throws<ArgumentException>(() => StringEnum.Parse("false"));
            Assert.DoesNotThrow(() => StringEnum.Parse("Test"));
        }
        [Test]
        public void try_parse()
        {
            StringEnum @enum;
            Assert.False(StringEnum.TryParse("one", out @enum));
            Assert.True(StringEnum.TryParse("Test", out @enum));
        }

    }
}
