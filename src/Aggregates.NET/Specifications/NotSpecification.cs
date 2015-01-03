using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Specifications.Expressions;

namespace Aggregates.Specifications
{
    [Serializable]
    public class NegateSpecification<T> : Specification<T>
    {
        private readonly Specification<T> spec;

        public NegateSpecification(Specification<T> spec)
        {
            this.spec = spec;
        }
        public override Expression<Func<T, bool>> Predicate
        {
            get
            {
                var pred = spec.Predicate;
                return Expression.Lambda<Func<T, bool>>(
                    Expression.Not(pred.Body), pred.Parameters);
            }
        }

        protected override object[] Parameters
        {
            get
            {
                return new[] { spec };
            }
        }
    }
}
