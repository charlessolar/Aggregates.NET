using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Contracts
{
    public enum Lifestyle
    {
        PerInstance,
        Singleton,
        UnitOfWork
    }
    public interface IContainer : IDisposable
    {
        void Register(Type concrete, Lifestyle lifestyle);
        void Register<TInterface>(TInterface instance, Lifestyle lifestyle) where TInterface : class;
        void Register<TInterface, TConcrete>(Lifestyle lifestyle, string name = null) where TInterface : class where TConcrete : class, TInterface;
        void Register<TInterface>(Func<IContainer, TInterface> factory, Lifestyle lifestyle, string name = null) where TInterface : class;

        object Resolve(Type resolve);
        TResolve Resolve<TResolve>() where TResolve : class;
        IEnumerable<TResolve> ResolveAll<TResolve>() where TResolve : class;

        object TryResolve(Type resolve);
        TResolve TryResolve<TResolve>() where TResolve : class;

        IContainer GetChildContainer();
    }
}
