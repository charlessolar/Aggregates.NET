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
        void Register(Type concrete, object instance, Lifestyle lifestyle);
        void Register<TInterface>(TInterface instance, Lifestyle lifestyle);
        void Register<TInterface, TConcrete>(Lifestyle lifestyle, string name = null);
        void Register<TInterface>(Func<IContainer, TInterface> factory, Lifestyle lifestyle, string name = null);

        bool HasService(Type serviceType);

        object Resolve(Type resolve);
        TResolve Resolve<TResolve>();
        IEnumerable<TResolve> ResolveAll<TResolve>();
        IEnumerable<object> ResolveAll(Type resolve);

        object TryResolve(Type resolve);
        TResolve TryResolve<TResolve>();

        IContainer GetChildContainer();
    }
}

