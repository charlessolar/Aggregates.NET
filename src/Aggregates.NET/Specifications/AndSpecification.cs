using System;
using System.Linq.Expressions;
using Aggregates.Specifications.Expressions.Combining;

namespace Aggregates.Specifications
{
    [Serializable]
    public class AndSpecification<T> : Specification<T>
    {
        private readonly Specification<T> _spec1;

        private readonly Specification<T> _spec2;

        public AndSpecification(Specification<T> spec1, Specification<T> spec2)
        {
            _spec1 = spec1;
            _spec2 = spec2;
        }

        protected override object[] Parameters => new[] { _spec1, _spec2 };

        public override Expression<Func<T, bool>> Predicate
        {
            get
            {
                var expr1 = _spec1.Predicate;
                var expr2 = _spec2.Predicate;

                // combines the expressions without the need for Expression.Invoke which fails on EntityFramework
                return expr1.AndAlso(expr2);
            }
        }
    }
}
