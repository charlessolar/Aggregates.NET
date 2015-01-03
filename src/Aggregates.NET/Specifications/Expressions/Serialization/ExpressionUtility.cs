using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;

namespace Aggregates.Specifications.Expressions
{
    /// <summary>
    /// ExpressionUtility
    /// </summary>
    public static class ExpressionUtility
    {

        #region "privates"

        private static MemberBinding EnsureBinding(MemberBinding binding)
        {
            switch (binding.BindingType)
            {
                case MemberBindingType.Assignment:
                    return EnsureMemberAssignment((MemberAssignment)binding);
                case MemberBindingType.MemberBinding:
                    return EnsureMemberMemberBinding((MemberMemberBinding)binding);
                case MemberBindingType.ListBinding:
                    return EnsureMemberListBinding((MemberListBinding)binding);
                default:
                    throw new NotSupportedException();
            }
        }

        private static ElementInit EnsureElementInitializer(ElementInit initializer)
        {
            var arguments = EnsureExpressionList(initializer.Arguments);

            return arguments != initializer.Arguments ? Expression.ElementInit(initializer.AddMethod, arguments) : initializer;
        }

        private static Expression EnsureUnary(UnaryExpression expression)
        {
            var operand = Ensure(expression.Operand);

            return operand != expression.Operand ? Expression.MakeUnary(expression.NodeType, operand, expression.Type, expression.Method) : expression;
        }

        private static Expression EnsureBinary(BinaryExpression expression)
        {
            var left = Ensure(expression.Left);
            dynamic right = Ensure(expression.Right);

            if (right is MemberExpression)
            {
                var info = (FieldInfo)right.Member;
                var constant = Expression.Constant((info.GetValue(((ConstantExpression)right.Expression).Value)));

                return Expression.MakeBinary(expression.NodeType, expression.Left, constant);
            }

            right = Ensure(expression.Right);

            var conversion = Ensure(expression.Conversion);

            if (left != expression.Left || right != expression.Right || conversion != expression.Conversion)
            {
                if (expression.NodeType == ExpressionType.Coalesce && expression.Conversion != null)
                    return Expression.Coalesce(left, right, conversion as LambdaExpression);

                return Expression.MakeBinary(expression.NodeType, left, right, expression.IsLiftedToNull, expression.Method);
            }

            return expression;
        }

        private static Expression EnsureTypeIs(TypeBinaryExpression expression)
        {
            var expr = Ensure(expression.Expression);

            return expr != expression.Expression ? Expression.TypeIs(expr, expression.TypeOperand) : expression;
        }

        private static Expression EnsureConstant(Expression expression)
        {
            if (expression == null)
                throw new ArgumentNullException("expression");

            return expression;
        }

        private static Expression EnsureConditional(ConditionalExpression expression)
        {
            var test = Ensure(expression.Test);
            var ifTrue = Ensure(expression.IfTrue);
            var ifFalse = Ensure(expression.IfFalse);

            return test != expression.Test || ifTrue != expression.IfTrue || ifFalse != expression.IfFalse
            ? Expression.Condition(test, ifTrue, ifFalse)
            : expression;
        }

        private static Expression EnsureParameter(Expression expression)
        {
            if (expression == null)
                throw new ArgumentNullException("expression");

            return expression;
        }

        private static Expression EnsureMemberAccess(MemberExpression expression)
        {
            var exp = Ensure(expression.Expression);

            return exp != expression.Expression ? Expression.MakeMemberAccess(exp, expression.Member) : expression;
        }

        private static Expression EnsureMethodCall(MethodCallExpression expression)
        {
            var obj = Ensure(expression.Object);
            var args = EnsureExpressionList(expression.Arguments);

            return obj != expression.Object || args != expression.Arguments
            ? Expression.Call(obj, expression.Method, args)
            : expression;
        }

        private static IEnumerable<Expression> EnsureExpressionList(ReadOnlyCollection<Expression> original)
        {
            List<Expression> list = null;
            var n = original.Count;

            for (var i = 0; i < n; i++)
            {
                var p = Ensure(original[i]);

                if (list != null)
                    list.Add(p);
                else if (p != original[i])
                {
                    list = new List<Expression>(n);

                    for (var j = 0; j < i; j++)
                        list.Add(original[j]);

                    list.Add(p);
                }
            }

            return list != null ? list.AsReadOnly() : original;
        }

        private static MemberAssignment EnsureMemberAssignment(MemberAssignment assignment)
        {
            var e = Ensure(assignment.Expression);

            return e != assignment.Expression ? Expression.Bind(assignment.Member, e) : assignment;
        }

        private static MemberMemberBinding EnsureMemberMemberBinding(MemberMemberBinding binding)
        {
            var bindings = EnsureBindingList(binding.Bindings);

            return bindings != binding.Bindings ? Expression.MemberBind(binding.Member, bindings) : binding;
        }

        private static MemberListBinding EnsureMemberListBinding(MemberListBinding binding)
        {
            var initializers = EnsureElementInitializerList(binding.Initializers);

            return initializers != binding.Initializers ? Expression.ListBind(binding.Member, initializers) : binding;
        }

        private static IEnumerable<MemberBinding> EnsureBindingList(ReadOnlyCollection<MemberBinding> original)
        {
            List<MemberBinding> list = null;
            var n = original.Count;

            for (var i = 0; i < n; i++)
            {
                var b = EnsureBinding(original[i]);

                if (list != null)
                    list.Add(b);
                else if (b != original[i])
                {
                    list = new List<MemberBinding>(n);

                    for (var j = 0; j < i; j++)
                        list.Add(original[j]);

                    list.Add(b);
                }
            }

            return list != null ? (IEnumerable<MemberBinding>)list : original;
        }

        private static IEnumerable<ElementInit> EnsureElementInitializerList(ReadOnlyCollection<ElementInit> original)
        {
            List<ElementInit> list = null;
            var n = original.Count;

            for (var i = 0; i < n; i++)
            {
                var init = EnsureElementInitializer(original[i]);

                if (list != null)
                    list.Add(init);
                else if (init != original[i])
                {
                    list = new List<ElementInit>(n);

                    for (var j = 0; j < i; j++)
                        list.Add(original[j]);

                    list.Add(init);
                }
            }

            return list != null ? (IEnumerable<ElementInit>)list : original;
        }

        private static Expression EnsureLambda(LambdaExpression expression)
        {
            var body = Ensure(expression.Body);

            return body != expression.Body ? Expression.Lambda(expression.Type, body, expression.Parameters) : expression;
        }

        private static NewExpression EnsureNew(NewExpression expression)
        {
            var args = EnsureExpressionList(expression.Arguments);

            if (args != expression.Arguments)
                return expression.Members != null ? Expression.New(expression.Constructor, args, expression.Members) : Expression.New(expression.Constructor, args);

            return expression;
        }

        private static Expression EnsureMemberInit(MemberInitExpression expression)
        {
            var n = EnsureNew(expression.NewExpression);
            var bindings = EnsureBindingList(expression.Bindings);

            return n != expression.NewExpression || bindings != expression.Bindings
            ? Expression.MemberInit(n, bindings)
            : expression;
        }

        private static Expression EnsureListInit(ListInitExpression expression)
        {
            var n = EnsureNew(expression.NewExpression);
            var initializers = EnsureElementInitializerList(expression.Initializers);

            return n != expression.NewExpression || initializers != expression.Initializers
            ? Expression.ListInit(n, initializers)
            : expression;
        }

        private static Expression EnsureNewArray(NewArrayExpression expression)
        {
            var exprs = EnsureExpressionList(expression.Expressions);

            if (exprs != expression.Expressions)
                return expression.NodeType == ExpressionType.NewArrayInit ? Expression.NewArrayInit(expression.Type.GetElementType(), exprs) : Expression.NewArrayBounds(expression.Type.GetElementType(), exprs);

            return expression;
        }

        private static Expression EnsureInvocation(InvocationExpression expression)
        {
            var args = EnsureExpressionList(expression.Arguments);
            var expr = Ensure(expression.Expression);

            return args != expression.Arguments || expr != expression.Expression
            ? Expression.Invoke(expr, args)
            : expression;
        }

        #endregion

        /// <summary>
        /// Ensure
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        public static Expression Ensure(Expression expression)
        {
            if (expression == null)
                return expression;

            switch (expression.NodeType)
            {
                case ExpressionType.Negate:
                case ExpressionType.NegateChecked:
                case ExpressionType.Not:
                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                case ExpressionType.ArrayLength:
                case ExpressionType.Quote:
                case ExpressionType.TypeAs:
                    return EnsureUnary((UnaryExpression)expression);
                case ExpressionType.Add:
                case ExpressionType.AddChecked:
                case ExpressionType.Subtract:
                case ExpressionType.SubtractChecked:
                case ExpressionType.Multiply:
                case ExpressionType.MultiplyChecked:
                case ExpressionType.Divide:
                case ExpressionType.Modulo:
                case ExpressionType.And:
                case ExpressionType.AndAlso:
                case ExpressionType.Or:
                case ExpressionType.OrElse:
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                case ExpressionType.Coalesce:
                case ExpressionType.ArrayIndex:
                case ExpressionType.RightShift:
                case ExpressionType.LeftShift:
                case ExpressionType.ExclusiveOr:
                    return EnsureBinary((BinaryExpression)expression);
                case ExpressionType.TypeIs:
                    return EnsureTypeIs((TypeBinaryExpression)expression);
                case ExpressionType.Conditional:
                    return EnsureConditional((ConditionalExpression)expression);
                case ExpressionType.Constant:
                    return EnsureConstant((ConstantExpression)expression);
                case ExpressionType.Parameter:
                    return EnsureParameter((ParameterExpression)expression);
                case ExpressionType.MemberAccess:
                    return EnsureMemberAccess((MemberExpression)expression);
                case ExpressionType.Call:
                    return EnsureMethodCall((MethodCallExpression)expression);
                case ExpressionType.Lambda:
                    return EnsureLambda((LambdaExpression)expression);
                case ExpressionType.New:
                    return EnsureNew((NewExpression)expression);
                case ExpressionType.NewArrayInit:
                case ExpressionType.NewArrayBounds:
                    return EnsureNewArray((NewArrayExpression)expression);
                case ExpressionType.Invoke:
                    return EnsureInvocation((InvocationExpression)expression);
                case ExpressionType.MemberInit:
                    return EnsureMemberInit((MemberInitExpression)expression);
                case ExpressionType.ListInit:
                    return EnsureListInit((ListInitExpression)expression);
                default:
                    throw new NotSupportedException();
            }
        }
    }


}