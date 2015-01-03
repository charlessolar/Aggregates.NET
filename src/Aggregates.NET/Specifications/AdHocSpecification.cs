using System;
using System.Linq.Expressions;
using System.Xml.Linq;
using Aggregates.Specifications.Expressions;

namespace Aggregates.Specifications
{
	[Serializable]
	public class AdHocSpecification<T> : Specification<T>
	{
		//private readonly Expression<Func<T, bool>> specification;
	    private readonly String serializedExpressionXml;

		public AdHocSpecification(Expression<Func<T, bool>> specification)
		{

		    var cleanedExpression = ExpressionUtility.Ensure(specification);

            //this.specification = specification;
		    var serializer = new ExpressionSerializer();
		    var serializedExpression = serializer.Serialize(cleanedExpression);
		    serializedExpressionXml = serializedExpression.ToString();
		}

		public override Expression<Func<T, bool>> Predicate
        {
            get
            {
                var serializer = new ExpressionSerializer();
                var serializedExpression = XElement.Parse(serializedExpressionXml);
                var specification = serializer.Deserialize<Func<T, bool>>(serializedExpression);
                return specification;
            }
		}
	}
}