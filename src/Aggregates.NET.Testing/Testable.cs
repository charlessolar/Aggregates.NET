using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    public interface IChecker
    {
        IChecker Raised<TEvent>(Action<TEvent> factory) where TEvent : Messages.IEvent;
    }
    public interface IEventPlanner 
    {
        IEventPlanner HasEvent<TEvent>(Action<TEvent> factory);
        IEventPlanner HasSnapshot(object snapshot);
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
        IChecker Check(Id id);
        IChecker Check(string bucket, Id id);
        IEventPlanner Plan(Id id);
        IEventPlanner Plan(string bucket, Id id);
    }
    public interface IRepositoryTest<TEntity, TParent> : IRepository where TParent : IEntity where TEntity : IChildEntity<TParent>
    {
        IChecker Check(Id id);
        IEventPlanner Plan(Id id);
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
