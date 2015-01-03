using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

// Most of this is from LinqSpecs http://linqspecs.codeplex.com/ with modifications for use inside aggregates/entities
namespace Aggregates.Specifications
{
    [Serializable]
    public abstract class Specification<T>
    {
        private Func<T, bool> _compiledExpression;
        private Func<T, bool> CompiledExpression
        {
            get { return _compiledExpression ?? (_compiledExpression = Predicate.Compile()); }
        }

        public bool IsSatisfiedBy(T obj)
        {
            return CompiledExpression(obj);
        }

        public abstract Expression<Func<T, bool>> Predicate { get; }

        public static Specification<T> operator &(Specification<T> spec1, Specification<T> spec2)
        {
            return new AndSpecification<T>(spec1, spec2);
        }

        public static bool operator false(Specification<T> spec1)
        {
            return false; // no-op. & and && do exactly the same thing.
        }

        public static bool operator true(Specification<T> spec1)
        {
            return false; // no-op. & and && do exactly the same thing.
        }

        public static Specification<T> operator |(Specification<T> spec1, Specification<T> spec2)
        {
            return new OrSpecification<T>(spec1, spec2);
        }

        public static Specification<T> operator !(Specification<T> spec1)
        {
            return new NegateSpecification<T>(spec1);
        }

        protected virtual object[] Parameters { get { return new object[] { Guid.NewGuid() }; } }

        public bool Equals(Specification<T> other)
        {
            return Parameters.SequenceEqual(other.Parameters);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((Specification<T>)obj);
        }

        public override int GetHashCode()
        {
            return Parameters.GetHashCode();
        }

    }
}
