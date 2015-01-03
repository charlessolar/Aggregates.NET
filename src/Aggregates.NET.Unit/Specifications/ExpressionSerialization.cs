using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using NUnit.Framework;
using Aggregates.Specifications;

namespace LinqSpecs.Tests
{
    [TestFixture]
    public class ExpressionSerialization
    {
        [Test]
        public void SimpleAdHocSpecificationIsSerializable()
        {
            var testSpecification = new AdHocSpecification<string>(n => n == "it works");

            // serialize and deserialize the spec
            var serializedSpecification = Serialize(testSpecification);
            var deserializedSpecification = Deserialize<Specification<string>>(serializedSpecification);

            Assert.That(deserializedSpecification.IsSatisfiedBy("it works"), Is.True);
            Assert.That(deserializedSpecification.IsSatisfiedBy("it fails"), Is.False);
        }

        [Test]
        public void NeagtedAdHocSpecificationIsSerializable()
        {
            var testSpecification1 = new AdHocSpecification<string>(n => n == "it fails");
            var testSpecification = new NegateSpecification<string>(testSpecification1);

            // serialize and deserialize the spec
            var serializedSpecification = Serialize(testSpecification);
            var deserializedSpecification = Deserialize<Specification<string>>(serializedSpecification);

            Assert.That(deserializedSpecification.IsSatisfiedBy("it works"), Is.True);
            Assert.That(deserializedSpecification.IsSatisfiedBy("it fails"), Is.False);
        }

        [Test]
        public void CombinedSpecificationIsSerializable()
        {
            var testSpecification1 = new AdHocSpecification<string>(n => n.StartsWith("it works"));
            var testSpecification2 = new AdHocSpecification<string>(n => n.EndsWith("very well"));
            var testSpecification = new AndSpecification<string>(testSpecification1, testSpecification2);

            // serialize and deserialize the spec
            var serializedSpecification = Serialize(testSpecification);
            var deserializedSpecification = Deserialize<Specification<string>>(serializedSpecification);

            Assert.That(deserializedSpecification.IsSatisfiedBy("it works very well"), Is.True);
            Assert.That(deserializedSpecification.IsSatisfiedBy("it works very well if you do it right"), Is.False);
        }


        // helper to binary serialize an object
        private static Stream Serialize(Object testSpecification)
        {
            var serializedData = new MemoryStream();
            new BinaryFormatter().Serialize(serializedData, testSpecification);
            return serializedData;
        }

        // helper to deserialize binary data to an object
        private static T Deserialize<T>(Stream serializedObject) where T : class
        {
            serializedObject.Seek(0, SeekOrigin.Begin);
            var deserializedObject = new BinaryFormatter().Deserialize(serializedObject);
            return deserializedObject as T;
        }

    }
}