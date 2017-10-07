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

namespace Aggregates.Extensions
{
    static class ReflectionExtensions
    {
        // some code from https://github.com/mfelicio/NDomain/blob/d30322bc64105ad2e4c961600ae24831f675b0e9/source/NDomain/Helpers/ReflectionUtils.cs

        public static Dictionary<string, Action<TState, object>> GetStateMutators<TState>() where TState : IState
        {
            var methods = typeof(TState)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(
                    m => (m.Name == "Handle" || m.Name == "Conflict") &&
                         m.GetParameters().Length == 1 &&
                         m.ReturnType == typeof(void))
                .ToArray();

            var stateEventMutators = from method in methods
                let eventType = method.GetParameters()[0].ParameterType
                select new
                {
                    Name = eventType.Name,
                    Type = method.Name,
                    Handler = BuildStateEventMutatorHandler<TState>(eventType, method)
                };

            return stateEventMutators.ToDictionary(m => $"{m.Type}.{m.Name}", m => m.Handler);
        }

        public static Func<object, TQuery, IDomainUnitOfWork, Task<TResponse>> MakeQueryHandler<TQuery, TResponse>(Type queryHandler) where TQuery : IQuery<TResponse>
        {
            var method = queryHandler
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(
                    m => (m.Name == "Handle") && 
                    m.GetParameters()[0].ParameterType == typeof(TQuery) && 
                    m.ReturnType == typeof(Task<TResponse>))
                .SingleOrDefault();

            if (method == null) return null;

            var handlerParam = Expression.Parameter(typeof(object), "handler");
            var queryParam = Expression.Parameter(typeof(TQuery), "query");
            var uowParam = Expression.Parameter(typeof(IDomainUnitOfWork), "uow");

            var castTarget = Expression.Convert(handlerParam, queryHandler);

            var body = Expression.Call(castTarget, method, queryParam, uowParam);

            return Expression.Lambda<Func<object, TQuery, IDomainUnitOfWork, Task<TResponse>>>(body, handlerParam, queryParam, uowParam).Compile();
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

        public static Func<IMetrics, IStoreEvents, IStoreSnapshots, IEventFactory, IDomainUnitOfWork, IRepository<TEntity>> BuildRepositoryFunc<TEntity>()
            where TEntity : IEntity
        {
            var stateType = typeof(TEntity).BaseType.GetGenericArguments()[1];
            var repoType = typeof(Repository<,>).MakeGenericType(typeof(TEntity), stateType);

            // doing my own open-generics implementation so I don't have to depend on an IoC container supporting it
            var ctor = repoType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(IMetrics), typeof(IStoreEvents), typeof(IStoreSnapshots), typeof(IEventFactory), typeof(IDomainUnitOfWork) }, null);
            if (ctor == null)
                throw new AggregateException("No constructor found for repository");

            var metricsParam = Expression.Parameter(typeof(IMetrics), "metrics");
            var eventstoreParam = Expression.Parameter(typeof(IStoreEvents), "eventstore");
            var snapshotParam = Expression.Parameter(typeof(IStoreSnapshots), "snapstore");
            var oobParam = Expression.Parameter(typeof(IOobWriter), "oobStore");
            var factoryParam = Expression.Parameter(typeof(IEventFactory), "factory");
            var uowParam = Expression.Parameter(typeof(IDomainUnitOfWork), "uow");

            var body = Expression.New(ctor, metricsParam, eventstoreParam, snapshotParam, oobParam, factoryParam, uowParam);
            var lambda = Expression.Lambda<Func<IMetrics, IStoreEvents, IStoreSnapshots, IEventFactory, IDomainUnitOfWork, IRepository<TEntity>>>(body, metricsParam, eventstoreParam, snapshotParam, oobParam, factoryParam, uowParam);

            return lambda.Compile();
        }
        public static Func<TParent, IMetrics, IStoreEvents, IStoreSnapshots, IOobWriter, IEventFactory, IDomainUnitOfWork, IRepository<TEntity, TParent>> BuildParentRepositoryFunc<TEntity, TParent>()
            where TEntity : IChildEntity<TParent> where TParent : IEntity
        {
            var stateType = typeof(TEntity).BaseType.GetGenericArguments()[1];
            var repoType = typeof(Repository<,,>).MakeGenericType(typeof(TEntity), stateType, typeof(TParent));

            // doing my own open-generics implementation so I don't have to depend on an IoC container supporting it
            var ctor = repoType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(TParent), typeof(IMetrics), typeof(IStoreEvents), typeof(IStoreSnapshots), typeof(IEventFactory), typeof(IDomainUnitOfWork) }, null);
            if (ctor == null)
                throw new AggregateException("No constructor found for repository");

            var parentParam = Expression.Parameter(typeof(TParent), "parent");
            var metricsParam = Expression.Parameter(typeof(IMetrics), "metrics");
            var eventstoreParam = Expression.Parameter(typeof(IStoreEvents), "eventstore");
            var snapshotParam = Expression.Parameter(typeof(IStoreSnapshots), "snapstore");
            var oobParam = Expression.Parameter(typeof(IOobWriter), "oobStore");
            var factoryParam = Expression.Parameter(typeof(IEventFactory), "factory");
            var uowParam = Expression.Parameter(typeof(IDomainUnitOfWork), "uow");

            var body = Expression.New(ctor, parentParam, metricsParam, eventstoreParam, snapshotParam, oobParam, factoryParam, uowParam);
            var lambda = Expression.Lambda<Func<TParent, IMetrics, IStoreEvents, IStoreSnapshots, IOobWriter, IEventFactory, IDomainUnitOfWork, IRepository<TEntity, TParent>>>(body, parentParam, metricsParam, eventstoreParam, snapshotParam, oobParam, factoryParam, uowParam);

            return lambda.Compile();
        }
        public static Func<IMetrics, IStoreEvents, IStoreSnapshots, IDomainUnitOfWork, IPocoRepository<T>> BuildPocoRepositoryFunc<T>()
            where T : class, new()
        {
            var repoType = typeof(PocoRepository<>).MakeGenericType(typeof(T));

            // doing my own open-generics implementation so I don't have to depend on an IoC container supporting it
            var ctor = repoType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(IMetrics), typeof(IStorePocos), typeof(IMessageSerializer), typeof(IDomainUnitOfWork) }, null);
            if (ctor == null)
                throw new AggregateException("No constructor found for repository");

            var metricsParam = Expression.Parameter(typeof(IMetrics), "metrics");
            var pocostoreParam = Expression.Parameter(typeof(IMessageSerializer), "pocostore");
            var serializerParam = Expression.Parameter(typeof(IMessageSerializer), "serializer");
            var uowParam = Expression.Parameter(typeof(IDomainUnitOfWork), "uow");

            var body = Expression.New(ctor, metricsParam, pocostoreParam, serializerParam, uowParam);
            var lambda = Expression.Lambda<Func<IMetrics, IStoreEvents, IStoreSnapshots, IDomainUnitOfWork, IPocoRepository<T>>>(body, metricsParam, pocostoreParam, serializerParam, uowParam);

            return lambda.Compile();
        }
        public static Func<TParent, IMetrics, IStoreEvents, IStoreSnapshots, IDomainUnitOfWork, IPocoRepository<T, TParent>> BuildParentPocoRepositoryFunc<T, TParent>()
            where T : class, new() where TParent : IEntity
        {
            var repoType = typeof(PocoRepository<,>).MakeGenericType(typeof(T), typeof(TParent));

            // doing my own open-generics implementation so I don't have to depend on an IoC container supporting it
            var ctor = repoType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(TParent), typeof(IMetrics), typeof(IStorePocos), typeof(IMessageSerializer), typeof(IDomainUnitOfWork) }, null);
            if (ctor == null)
                throw new AggregateException("No constructor found for repository");

            var parentParam = Expression.Parameter(typeof(TParent), "parent");
            var metricsParam = Expression.Parameter(typeof(IMetrics), "metrics");
            var pocostoreParam = Expression.Parameter(typeof(IStorePocos), "pocostore");
            var serializerParam = Expression.Parameter(typeof(IMessageSerializer), "serializer");
            var uowParam = Expression.Parameter(typeof(IDomainUnitOfWork), "uow");

            var body = Expression.New(ctor, parentParam, metricsParam, pocostoreParam, serializerParam, uowParam);
            var lambda = Expression.Lambda<Func<TParent, IMetrics, IStoreEvents, IStoreSnapshots, IDomainUnitOfWork, IPocoRepository<T, TParent>>>(body, parentParam, metricsParam, pocostoreParam, serializerParam, uowParam);

            return lambda.Compile();
        }
    }
}
