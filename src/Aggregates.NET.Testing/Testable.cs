using Aggregates.Contracts;
using Aggregates.Internal;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    public interface IChecker<TEntity> where TEntity : IEntity
    {
        IChecker<TChild> Check<TChild>(Id id) where TChild : IEntity, IChildEntity<TEntity>;
        IChecker<TChild> Check<TChild>(TestableId id) where TChild : IEntity, IChildEntity<TEntity>;
        /// <summary>
        /// Check that a specific event was raised
        /// </summary>
        IChecker<TEntity> Raised<TEvent>(Action<TEvent> factory) where TEvent : Messages.IEvent;
        /// <summary>
        /// Check that a specific type of event was raised
        /// </summary>
        IChecker<TEntity> Raised<TEvent>() where TEvent : Messages.IEvent;
        /// <summary>
        /// Check a property on a raised event
        /// </summary>
        IChecker<TEntity> Raised<TEvent>(Func<TEvent, bool> assert) where TEvent : Messages.IEvent;
    }
    public interface IEventPlanner<TEntity> where TEntity : IEntity
    {
        IEventPlanner<TChild> Plan<TChild>(Id id) where TChild : IEntity, IChildEntity<TEntity>;
        IEventPlanner<TChild> Plan<TChild>(TestableId id) where TChild : IEntity, IChildEntity<TEntity>;
        /// <summary>
        /// entity will exist to read - for when events arn't needed
        /// </summary>
        IEventPlanner<TEntity> Exists();
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
        IChecker<TEntity> Check(TestableId id);
        IChecker<TEntity> Check(string bucket, Id id);
        IChecker<TEntity> Check(string bucket, TestableId id);
        IEventPlanner<TEntity> Plan(Id id);
        IEventPlanner<TEntity> Plan(TestableId id);
        IEventPlanner<TEntity> Plan(string bucket, Id id);
        IEventPlanner<TEntity> Plan(string bucket, TestableId id);
    }
    public interface IRepositoryTest<TEntity, TParent> : IRepository where TParent : IEntity where TEntity : IEntity, IChildEntity<TParent>
    {
        IChecker<TEntity> Check(Id id);
        IChecker<TEntity> Check(TestableId id);
        IEventPlanner<TEntity> Plan(Id id);
        IEventPlanner<TEntity> Plan(TestableId id);
    }
    public interface IPocoRepositoryTest<T> where T : class, new()
    {
        IPocoChecker Check(Id id);
        IPocoChecker Check(TestableId id);
        IPocoChecker Check(string bucket, Id id);
        IPocoChecker Check(string bucket, TestableId id);
        IPocoPlanner Plan(Id id);
        IPocoPlanner Plan(TestableId id);
        IPocoPlanner Plan(string bucket, Id id);
        IPocoPlanner Plan(string bucket, TestableId id);
    }
    public interface IPocoRepositoryTest<T, TParent> where TParent : IEntity where T : class, new()
    {
        IPocoChecker Check(Id id);
        IPocoChecker Check(TestableId id);
        IPocoPlanner Plan(Id id);
        IPocoPlanner Plan(TestableId id);
    }
}
