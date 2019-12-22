using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Internal
{
    class TestableContainer : IContainer
    {
        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public IContainer GetChildContainer()
        {
            throw new NotImplementedException();
        }

        public bool HasService(Type serviceType)
        {
            throw new NotImplementedException();
        }

        public void Register(Type concrete, Lifestyle lifestyle)
        {
            throw new NotImplementedException();
        }

        public void Register<TInterface>(TInterface instance, Lifestyle lifestyle)
        {
            throw new NotImplementedException();
        }

        public void Register<TInterface>(Func<IContainer, TInterface> factory, Lifestyle lifestyle, string name = null)
        {
            throw new NotImplementedException();
        }

        public void Register(Type concrete, object instance, Lifestyle lifestyle)
        {
            throw new NotImplementedException();
        }

        public object Resolve(Type resolve)
        {
            throw new NotImplementedException();
        }

        public TResolve Resolve<TResolve>()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<TResolve> ResolveAll<TResolve>()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<object> ResolveAll(Type resolve)
        {
            throw new NotImplementedException();
        }

        public object TryResolve(Type resolve)
        {
            throw new NotImplementedException();
        }

        public TResolve TryResolve<TResolve>()
        {
            throw new NotImplementedException();
        }

        void IContainer.Register<TInterface, TConcrete>(Lifestyle lifestyle, string name)
        {
            throw new NotImplementedException();
        }
    }
}
