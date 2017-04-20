using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
// ReSharper disable InconsistentNaming

namespace Aggregates.Extensions
{
    // Compile class methods into delegates == MUCH faster than MethodInfo.Invoke
    static class MethodInfoExtensions
    {
        public static Func<TTarget, TReturn> MakeFuncDelegate<TTarget, TReturn>(this MethodInfo method)
        {
            var target = Expression.Parameter(typeof(TTarget));

            Expression body = Expression.Call(target, method);

            return Expression.Lambda<Func<TTarget, TReturn>>(body, target).Compile();
        }
        public static Func<object, TReturn> MakeFuncDelegateWithTarget<TReturn>(this MethodInfo method, Type targetType)
        {
            var target = Expression.Parameter(typeof(object));

            var castTarget = Expression.Convert(target, targetType);

            Expression body = Expression.Call(castTarget, method);

            return Expression.Lambda<Func<object, TReturn>>(body, target).Compile();
        }



        public static Func<TTarget, TParam1, TReturn> MakeFuncDelegate<TTarget, TParam1, TReturn>(this MethodInfo method)
        {
            var target = Expression.Parameter(typeof(TTarget));
            var param1 = Expression.Parameter(typeof(TParam1));

            Expression body = Expression.Call(target, method, param1);

            return Expression.Lambda<Func<TTarget, TParam1, TReturn>>(body, target, param1).Compile();
        }
        public static Func<object, TParam1, TReturn> MakeFuncDelegateWithTarget<TParam1, TReturn>(this MethodInfo method, Type targetType)
        {
            var target = Expression.Parameter(typeof(object));
            var param1 = Expression.Parameter(typeof(TParam1));

            var castTarget = Expression.Convert(target, targetType);

            Expression body = Expression.Call(castTarget, method, param1);

            return Expression.Lambda<Func<object, TParam1, TReturn>>(body, target, param1).Compile();
        }



        public static Func<TTarget, TParam1, TParam2, TReturn> MakeFuncDelegate<TTarget, TParam1, TParam2, TReturn>(this MethodInfo method)
        {
            var target = Expression.Parameter(typeof(TTarget));
            var param1 = Expression.Parameter(typeof(TParam1));
            var param2 = Expression.Parameter(typeof(TParam2));

            Expression body = Expression.Call(target, method, param1, param2);

            return Expression.Lambda<Func<TTarget, TParam1, TParam2, TReturn>>(body, target, param1, param2).Compile();
        }
        public static Func<object, TParam1, TParam2, TReturn> MakeFuncDelegateWithTarget<TParam1, TParam2, TReturn>(this MethodInfo method, Type targetType)
        {
            var target = Expression.Parameter(typeof(object));
            var param1 = Expression.Parameter(typeof(TParam1));
            var param2 = Expression.Parameter(typeof(TParam2));

            var castTarget = Expression.Convert(target, targetType);

            Expression body = Expression.Call(castTarget, method, param1, param2);

            return Expression.Lambda<Func<object, TParam1, TParam2, TReturn>>(body, target, param1, param2).Compile();
        }
    }
}
