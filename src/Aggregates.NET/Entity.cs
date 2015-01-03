using Aggregates.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    // Implementation from http://stackoverflow.com/a/2326321/223547
    public abstract class Entity<TId> : IEquatable<Entity<TId>>
    {
        private readonly TId _id;
        protected IList<Specification<Entity<TId>>> _specifications;


        protected Entity(TId id)
        {
            if (object.Equals(id, default(TId)))
            {
                throw new ArgumentException("The ID cannot be the default value.", "id");
            }

            this._id = id;
        }

        public TId Id
        {
            get { return this._id; }
        }



        
        public override int GetHashCode()
        {
            return this.Id.GetHashCode();
        }

        /// <inheritdoc />
        public bool Equals(Entity<TId> other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (ReferenceEquals(null, other)) return false;
            if (this.GetType() != other.GetType()) return false;

            return this.Id.Equals(other.Id);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return this.Equals(obj as Entity<TId>);
        }
    }
}
