using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    public interface IChecker<TEntity> where TEntity : IEntity
    {
        IChecker<TChild> Check<TChild>(Id id) where TChild : IEntity, IChildEntity<TEntity>;
        IChecker<TEntity> Raised<TEvent>(Action<TEvent> factory) where TEvent : Messages.IEvent;
    }
    public interface IEventPlanner<TEntity> where TEntity : IEntity
    {
        IEventPlanner<TChild> Plan<TChild>(Id id) where TChild : IEntity, IChildEntity<TEntity>;
        IEventPlanner<TEntity> HasEvent<TEvent>(Action<TEvent> factory);
        IEventPlanner<TEntity> HasSnapshot(object snapshot);
    }
    public interface IPocoChecker
    {
        IPocoChecker IsEqual(object poco);
    }
    public interface IPocoPlanner
    {
        IPocoPlanner HasValue(object poco);
    }


    public interface IRepositoryTest<TEntity> : IRepository where TEntity : IEntity
    {
        IChecker<TEntity> Check(Id id);
        IChecker<TEntity> Check(string bucket, Id id);
        IEventPlanner<TEntity> Plan(Id id);
        IEventPlanner<TEntity> Plan(string bucket, Id id);
    }
    public interface IRepositoryTest<TEntity, TParent> : IRepository where TParent : IEntity where TEntity : IEntity, IChildEntity<TParent>
    {
        IChecker<TEntity> Check(Id id);
        IEventPlanner<TEntity> Plan(Id id);
    }
    public interface IPocoRepositoryTest<T> where T : class, new()
    {
        IPocoChecker Check(Id id);
        IPocoChecker Check(string bucket, Id id);
        IPocoPlanner Plan(Id id);
        IPocoPlanner Plan(string bucket, Id id);
    }
    public interface IPocoRepositoryTest<T, TParent> where TParent : IEntity where T : class, new()
    {
        IPocoChecker Check(Id id);
        IPocoPlanner Plan(Id id);
    }
}
