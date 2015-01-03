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
    public class AndSpecification<T> : Specification<T>
    {
        private readonly Specification<T> spec1;

        private readonly Specification<T> spec2;

        public AndSpecification(Specification<T> spec1, Specification<T> spec2)
        {
            this.spec1 = spec1;
            this.spec2 = spec2;
        }

        protected override object[] Parameters
        {
            get
            {
                return new[] { spec1, spec2 };
            }
        }

        public override Expression<Func<T, bool>> Predicate
        {
            get
            {
                var expr1 = spec1.Predicate;
                var expr2 = spec2.Predicate;

                // combines the expressions without the need for Expression.Invoke which fails on EntityFramework
                return expr1.AndAlso(expr2);
            }
        }
    }
}
