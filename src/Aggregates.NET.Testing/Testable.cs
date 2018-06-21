using Aggregates.Contracts;
using Aggregates.Internal;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    public interface ITestableDomain : UnitOfWork.IDomain
    {
        IEventChecker<TEntity> Check<TEntity>(Id id) where TEntity : IEntity;
        IEventPlanner<TEntity> Plan<TEntity>(Id id) where TEntity : IEntity;
        IEventChecker<TEntity> Check<TEntity>(string bucket, Id id) where TEntity : IEntity;
        IEventPlanner<TEntity> Plan<TEntity>(string bucket, Id id) where TEntity : IEntity;
    }
    public interface ITestableApplication : UnitOfWork.IGeneric
    {
        IModelChecker<TModel> Check<TModel>(Id id) where TModel : class, new();
        IModelPlanner<TModel> Plan<TModel>(Id id) where TModel : class, new();
    }
    public interface ITestableProcessor : IProcessor
    {
        IServiceChecker<TService, TResponse> Check<TService, TResponse>(TService service) where TService : IService<TResponse>;
        IServicePlanner<TService, TResponse> Plan<TService, TResponse>(TService service) where TService : IService<TResponse>;
    }
    public interface IEventChecker<TEntity> where TEntity : IEntity
    {
        IEventChecker<TChild> Check<TChild>(Id id) where TChild : IEntity, IChildEntity<TEntity>;
        /// <summary>
        /// Check that a specific event was raised
        /// </summary>
        IEventChecker<TEntity> Raised<TEvent>(Action<TEvent> factory) where TEvent : Messages.IEvent;
        /// <summary>
        /// Check that a specific type of event was raised
        /// </summary>
        IEventChecker<TEntity> Raised<TEvent>() where TEvent : Messages.IEvent;
        /// <summary>
        /// Check a property on a raised event
        /// </summary>
        IEventChecker<TEntity> Raised<TEvent>(Func<TEvent, bool> assert) where TEvent : Messages.IEvent;
    }
    public interface IEventPlanner<TEntity> where TEntity : IEntity
    {
        IEventPlanner<TChild> Plan<TChild>(Id id) where TChild : IEntity, IChildEntity<TEntity>;
        /// <summary>
        /// entity will exist to read - for when events arn't needed
        /// </summary>
        IEventPlanner<TEntity> Exists();
        IEventPlanner<TEntity> HasEvent<TEvent>(Action<TEvent> factory);
        IEventPlanner<TEntity> HasSnapshot(object snapshot);
    }
    public interface IModelPlanner<TModel> where TModel : class, new()
    {
        IModelPlanner<TModel> Exists();
        IModelPlanner<TModel> Exists(TModel model);
    }
    public interface IModelChecker<TModel> where TModel : class, new()
    {
        IModelChecker<TModel> Added();
        IModelChecker<TModel> Added(Func<TModel, bool> assert);
        IModelChecker<TModel> Added(TModel model);
        IModelChecker<TModel> Updated();
        IModelChecker<TModel> Updated(Func<TModel, bool> assert);
        IModelChecker<TModel> Updated(TModel model);
        IModelChecker<TModel> Read();
        IModelChecker<TModel> Deleted();
    }
    public interface IServicePlanner<TService, TResponse> where TService : IService<TResponse>
    {
        IServicePlanner<TService, TResponse> Response(TResponse response);
    }
    public interface IServiceChecker<TService, TResponse> where TService : IService<TResponse>
    {
        IServiceChecker<TService, TResponse> Requested();
    }


    public interface IRepositoryTest<TEntity> : IRepository where TEntity : IEntity
    {
        IEventChecker<TEntity> Check(Id id);
        IEventChecker<TEntity> Check(TestableId id);
        IEventChecker<TEntity> Check(string bucket, Id id);
        IEventChecker<TEntity> Check(string bucket, TestableId id);
        IEventPlanner<TEntity> Plan(Id id);
        IEventPlanner<TEntity> Plan(TestableId id);
        IEventPlanner<TEntity> Plan(string bucket, Id id);
        IEventPlanner<TEntity> Plan(string bucket, TestableId id);
    }
    public interface IRepositoryTest<TEntity, TParent> : IRepository where TParent : IEntity where TEntity : IEntity, IChildEntity<TParent>
    {
        IEventChecker<TEntity> Check(Id id);
        IEventChecker<TEntity> Check(TestableId id);
        IEventPlanner<TEntity> Plan(Id id);
        IEventPlanner<TEntity> Plan(TestableId id);
    }
}
