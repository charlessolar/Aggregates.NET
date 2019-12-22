using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
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
        public void Register(Type serviceType, object instance, Contracts.Lifestyle lifestyle)
        {
            _container.Configure(x => x.For(serviceType).Use(instance).SetLifecycleTo(ConvertLifestyle(lifestyle)));
        }
        public void Register<TInterface>(TInterface instance, Contracts.Lifestyle lifestyle) 
        {
            _container.Configure(x => x.For<TInterface>().Use(() => instance).SetLifecycleTo(ConvertLifestyle(lifestyle)));
        }

        public void Register<TInterface>(Func<IContainer, TInterface> factory, Contracts.Lifestyle lifestyle, string name = null)
        {
            _container.Configure(x =>
            {
                var use = x.For<TInterface>().Use(y => factory(this));
                if (!string.IsNullOrEmpty(name))
                    use.Named(name);
                use.SetLifecycleTo(ConvertLifestyle(lifestyle));
            });
        }
        public void Register<TInterface, TConcrete>(Contracts.Lifestyle lifestyle, string name = null)
        {
            _container.Configure(x =>
            {
                var use = x.For(typeof(TInterface)).Use(typeof(TConcrete));
                if (!string.IsNullOrEmpty(name))
                    use.Named(name);
                use.SetLifecycleTo(ConvertLifestyle(lifestyle));
            });
        }
        public bool HasService(Type service)
        {
            return _container.GetInstance(service) != null;
        }

        public object Resolve(Type resolve)
        {
            return _container.GetInstance(resolve);
        }
        public TResolve Resolve<TResolve>()
        {
            return _container.GetInstance<TResolve>();
        }
        public IEnumerable<TResolve> ResolveAll<TResolve>()
        {
            return _container.GetAllInstances<TResolve>();
        }
        public IEnumerable<object> ResolveAll(Type service)
        {
            return (IEnumerable<object>)_container.GetAllInstances(service);
        }
        public object TryResolve(Type resolve)
        {
            return _container.TryGetInstance(resolve);
        }
        public TResolve TryResolve<TResolve>()
        {
            return _container.TryGetInstance<TResolve>();
        }

        public IContainer GetChildContainer()
        {
            return new Container(_container.GetNestedContainer());
        }
    }
}
