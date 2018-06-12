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

        public static Func<object, TParam1, TReturn> MakeFuncDelegateWithTarget<TParam1, TReturn>(this MethodInfo method, Type targetType)
        {
            var target = Expression.Parameter(typeof(object));
            var param1 = Expression.Parameter(typeof(TParam1));

            var castTarget = Expression.Convert(target, targetType);

            Expression body = Expression.Call(castTarget, method, param1);

            return Expression.Lambda<Func<object, TParam1, TReturn>>(body, target, param1).Compile();
        }

    }
}
