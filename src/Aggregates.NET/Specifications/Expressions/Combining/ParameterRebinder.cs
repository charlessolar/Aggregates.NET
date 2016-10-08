using System.Collections.Generic;
using System.Linq.Expressions;

namespace Aggregates.Specifications.Expressions.Combining
{

    // ------------------------------------------------------------------------------------------
    // This code was taken from the MSDN Blog meek: LINQ to Entities: Combining Predicates
    // http://blogs.msdn.com/b/meek/archive/2008/05/02/linq-to-entities-combining-predicates.aspx
    // ------------------------------------------------------------------------------------------
    
    public class ParameterRebinder : ExpressionVisitor {
        private readonly Dictionary<ParameterExpression, ParameterExpression> _map;
 
        public ParameterRebinder(Dictionary<ParameterExpression, ParameterExpression> map) {
            _map = map ?? new Dictionary<ParameterExpression, ParameterExpression>();
        }
 
        public static Expression ReplaceParameters(Dictionary<ParameterExpression, ParameterExpression> map, Expression exp) {
            return new ParameterRebinder(map).Visit(exp);
        }
 
        protected override Expression VisitParameter(ParameterExpression p) {
            ParameterExpression replacement;
            if (_map.TryGetValue(p, out replacement)) {
                p = replacement;
            }
            return base.VisitParameter(p);
        }
    }
}