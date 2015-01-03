using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.Entity
{
    public class EntityStub : Entity<Guid>
    {
        public EntityStub(Guid id) : base(id) { }
    }
    [TestFixture]
    public class Entity
    {
        [Test]
        public void entity_same()
        {
            var guid = Guid.NewGuid();
            var one = new EntityStub(guid);
            var two = new EntityStub(guid);

            Assert.AreEqual(one, two);
            Assert.True(one.Equals(two));
        }

        [Test]
        public void entity_different()
        {
            var one = new EntityStub(Guid.NewGuid());
            var two = new EntityStub(Guid.NewGuid());

            Assert.AreNotEqual(one, two);
            Assert.False(one.Equals(two));
        }

        [Test]
        public void entity_get_hash_equal()
        {
            var guid = Guid.NewGuid();
            var one = new EntityStub(guid);
            var two = new EntityStub(guid);

            Assert.AreEqual(one.GetHashCode(), two.GetHashCode());
        }

        [Test]
        public void entity_get_hash_different()
        {
            var one = new EntityStub(Guid.NewGuid());
            var two = new EntityStub(Guid.NewGuid());

            Assert.AreNotEqual(one.GetHashCode(), two.GetHashCode());
        }

        [Test]
        public void entity_default_throws()
        {
            Assert.Throws<ArgumentException>(() => new EntityStub(Guid.Empty));
        }
    }
}
