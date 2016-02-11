using Aggregates.Contracts;
using NServiceBus.MessageMutator;
using NServiceBus.UnitOfWork;
using System;
using System.Collections.Generic;
namespace Aggregates
{
    public interface IUnitOfWork : IDisposable, Contracts.IServiceProvider, IManageUnitsOfWork, IMutateTransportMessages, IMessageMutator
    {
        IRepository<T> For<T>() where T : class, IAggregate;

        void Commit();

        Object CurrentMessage { get; }
    }
}