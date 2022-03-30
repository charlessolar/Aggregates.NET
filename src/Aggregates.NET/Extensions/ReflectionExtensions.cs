using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Aggregates.Contracts;
using Aggregates.Messages;
using System.Threading.Tasks;
using Aggregates.Internal;
using Microsoft.Extensions.Logging;

namespace Aggregates.Extensions
{
    static class ReflectionExtensions
    {

        // some code from https://github.com/mfelicio/NDomain/blob/d30322bc64105ad2e4c961600ae24831f675b0e9/source/NDomain/Helpers/ReflectionUtils.cs

        public static Dictionary<string, Action<TState, object>> GetStateMutators<TState>() where TState : IState
        {

        var methods = typeof(TState)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(
                    m => (m.Name == "Handle"/* || m.Name == "Conflict"*/) &&
                         m.GetParameters().Length == 1 &&
                         m.ReturnType == typeof(void))
                .ToArray();

            var stateEventMutators = from method in methods
                where !method.IsPublic
                let eventType = method.GetParameters()[0].ParameterType
                select new
                {
                    Name = eventType.Name,
                    Type = method.Name,
                    Handler = BuildStateEventMutatorHandler<TState>(eventType, method)
                };

            return stateEventMutators.ToDictionary(m => $"{m.Type}.{m.Name}", m => m.Handler);
        }

        public static Func<object, TService, IServiceContext, Task<TResponse>> MakeServiceHandler<TService, TResponse>(Type queryHandler) where TService : IService<TResponse>
        {
            var method = queryHandler
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(
                    m => (m.Name == "Handle") && 
                    m.GetParameters()[0].ParameterType == typeof(TService) && 
                    m.ReturnType == typeof(Task<TResponse>))
                .SingleOrDefault();

            if (method == null) return null;

            var handlerParam = Expression.Parameter(typeof(object), "handler");
            var queryParam = Expression.Parameter(typeof(TService), "service");
            var contextParam = Expression.Parameter(typeof(IServiceContext), "context");

            var castTarget = Expression.Convert(handlerParam, queryHandler);

            var body = Expression.Call(castTarget, method, queryParam, contextParam);

            return Expression.Lambda<Func<object, TService, IServiceContext, Task<TResponse>>>(body, handlerParam, queryParam, contextParam).Compile();
        }

        private static Action<TState, object> BuildStateEventMutatorHandler<TState>(Type eventType, MethodInfo method)
            where TState : IState
        {
            var stateParam = Expression.Parameter(typeof(TState), "state");
            var eventParam = Expression.Parameter(typeof(object), "ev");

            // state.On((TEvent)ev)
            var methodCallExpr = Expression.Call(stateParam,
                method,
                Expression.Convert(eventParam, eventType));

            var lambda = Expression.Lambda<Action<TState, object>>(methodCallExpr, stateParam, eventParam);
            return lambda.Compile();
        }

        public static Func<TEntity> BuildCreateEntityFunc<TEntity>()
            where TEntity : IEntity
        {
            var ctor = typeof(TEntity).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { }, null);
            if (ctor == null)
                throw new AggregateException("Could not find constructor");
            

            var body = Expression.New(ctor);
            var lambda = Expression.Lambda<Func<TEntity>>(body);

            return lambda.Compile();
        }

        public static Func<ILogger, IStoreEntities, IRepository<TEntity>> BuildRepositoryFunc<TEntity>()
            where TEntity : IEntity
        {
            var stateType = typeof(TEntity).BaseType.GetGenericArguments()[1];
            var repoType = typeof(Repository<,>).MakeGenericType(typeof(TEntity), stateType);

            // doing my own open-generics implementation so I don't have to depend on an IoC container supporting it
            var ctor = repoType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ILogger), typeof(IStoreEntities) }, null);
            if (ctor == null)
                throw new AggregateException("No constructor found for repository");

            var param1 = Expression.Parameter(typeof(ILogger), "logger");
            var param2 = Expression.Parameter(typeof(IStoreEntities), "store");

            var body = Expression.New(ctor, param1, param2);
            var lambda = Expression.Lambda<Func<ILogger, IStoreEntities, IRepository<TEntity>>>(body, param1, param2);

            return lambda.Compile();
        }
        public static Func<ILogger, TParent, IStoreEntities, IRepository<TEntity, TParent>> BuildParentRepositoryFunc<TEntity, TParent>()
            where TEntity : IChildEntity<TParent> where TParent : IEntity
        {
            var stateType = typeof(TEntity).BaseType.GetGenericArguments()[1];
            var stateParentType = typeof(TParent).BaseType.GetGenericArguments()[1];
            var repoType = typeof(Repository<,,,>).MakeGenericType(typeof(TEntity), stateType, typeof(TParent), stateParentType);

            // doing my own open-generics implementation so I don't have to depend on an IoC container supporting it
            var ctor = repoType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(ILogger), typeof(TParent), typeof(IStoreEntities) }, null);
            if (ctor == null)
                throw new AggregateException("No constructor found for repository");

            var parentParam = Expression.Parameter(typeof(TParent), "parent");
            var param1 = Expression.Parameter(typeof(ILogger), "logger");
            var param2 = Expression.Parameter(typeof(IStoreEntities), "store");

            var body = Expression.New(ctor, param1, parentParam, param2);
            var lambda = Expression.Lambda<Func<ILogger, TParent, IStoreEntities, IRepository<TEntity, TParent>>>(body, param1, parentParam, param2);

            return lambda.Compile();
        }
    }
}
