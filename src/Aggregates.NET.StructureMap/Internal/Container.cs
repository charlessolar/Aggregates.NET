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

        public void Dispose()
        {
            _container.Dispose();
        }

        private StructureMap.Pipeline.ILifecycle ConvertLifestyle(Contracts.Lifestyle lifestyle)
        {
            switch (lifestyle)
            {
                case Contracts.Lifestyle.PerInstance:
                    return new StructureMap.Pipeline.TransientLifecycle();
                case Contracts.Lifestyle.Singleton:
                    return new StructureMap.Pipeline.SingletonLifecycle();
                case Contracts.Lifestyle.UnitOfWork:
                    // Transients are singletons in child containers
                    return new StructureMap.Pipeline.TransientLifecycle();
            }
            throw new ArgumentException($"Unknown lifestyle {lifestyle}");
        }

        public void Register(Type concrete, Contracts.Lifestyle lifestyle)
        {
            _container.Configure(x => x.For(concrete).Use(concrete).SetLifecycleTo(ConvertLifestyle(lifestyle)));
        }
        public void Register<TInterface>(TInterface instance, Contracts.Lifestyle lifestyle) where TInterface : class
        {
            _container.Configure(x => x.For<TInterface>().Use(instance).SetLifecycleTo(ConvertLifestyle(lifestyle)));
        }

        public void Register<TInterface>(Func<IContainer, TInterface> factory, Contracts.Lifestyle lifestyle, string name = null) where TInterface : class
        {
            _container.Configure(x =>
            {
                var use = x.For<TInterface>().Use(y => factory(this));
                if (!string.IsNullOrEmpty(name))
                    use.Named(name);
                use.SetLifecycleTo(ConvertLifestyle(lifestyle));
            });
        }
        public void Register<TInterface, TConcrete>(Contracts.Lifestyle lifestyle, string name = null) where TInterface : class where TConcrete : class, TInterface
        {
            _container.Configure(x =>
            {
                var use = x.For<TInterface>().Use<TConcrete>();
                if (!string.IsNullOrEmpty(name))
                    use.Named(name);
                use.SetLifecycleTo(ConvertLifestyle(lifestyle));
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
        public object TryResolve(Type resolve)
        {
            return _container.TryGetInstance(resolve);
        }
        public TResolve TryResolve<TResolve>() where TResolve : class
        {
            return _container.TryGetInstance<TResolve>();
        }

        public IContainer GetChildContainer()
        {
            return new Container(_container.GetNestedContainer());
        }
    }
}
