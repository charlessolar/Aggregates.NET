using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Internal
{
    class Container : IContainer
    {
        private readonly StructureMap.IContainer _container;

        public Container(StructureMap.IContainer container)
        {
            _container = container;
        }


        public void RegisterSingleton<TInterface>(TInterface instance, string name = null) where TInterface : class
        {
            _container.Configure(x =>
            {
                var use = x.For<TInterface>().Use(instance).Singleton();
                if (!string.IsNullOrEmpty(name))
                    use.Named(name);
            });
        }

        public void RegisterSingleton<TInterface>(Func<IContainer, TInterface> factory, string name = null) where TInterface : class
        {
            _container.Configure(x =>
            {
                var use = x.For<TInterface>().Use(y => factory(this)).Singleton();
                if (!string.IsNullOrEmpty(name))
                    use.Named(name);
            });
        }
        public void RegisterSingleton<TInterface, TConcrete>(string name = null) where TInterface : class where TConcrete : class, TInterface
        {
            _container.Configure(x =>
            {
                var use = x.For<TInterface>().Use<TConcrete>().Singleton();
                if (!string.IsNullOrEmpty(name))
                    use.Named(name);
            });
        }

        public void Register(Type concrete)
        {
            _container.Configure(x => x.For(concrete).Use(concrete));
        }

        public void Register<TInterface>(Func<IContainer, TInterface> factory, string name = null) where TInterface : class
        {
            _container.Configure(x =>
            {
                var use = x.For<TInterface>().Use(y => factory(this));
                if (!string.IsNullOrEmpty(name))
                    use.Named(name);
            });
        }
        public void Register<TInterface, TConcrete>(string name = null) where TInterface : class where TConcrete : class, TInterface
        {
            _container.Configure(x =>
            {
                var use = x.For<TInterface>().Use<TConcrete>();
                if (!string.IsNullOrEmpty(name))
                    use.Named(name);
            });
        }

        public object Resolve(Type resolve)
        {
            return _container.GetInstance(resolve);
        }
        public TResolve Resolve<TResolve>() where TResolve : class
        {
            return _container.GetInstance<TResolve>();
        }
        public IEnumerable<TResolve> ResolveAll<TResolve>() where TResolve : class
        {
            return _container.GetAllInstances<TResolve>();
        }

        public IContainer GetChildContainer()
        {
            return new Container(_container.GetNestedContainer());
        }
    }
}
