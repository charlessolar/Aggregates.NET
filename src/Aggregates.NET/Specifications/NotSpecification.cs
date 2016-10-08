using System;
using System.Linq.Expressions;

namespace Aggregates.Specifications
{
    [Serializable]
    public class NegateSpecification<T> : Specification<T>
    {
        private readonly Specification<T> _spec;

        public NegateSpecification(Specification<T> spec)
        {
            _spec = spec;
        }
        public override Expression<Func<T, bool>> Predicate
        {
            get
            {
                var pred = _spec.Predicate;
                return Expression.Lambda<Func<T, bool>>(
                    Expression.Not(pred.Body), pred.Parameters);
            }
        }

        protected override object[] Parameters => new[] { _spec };
    }
}
