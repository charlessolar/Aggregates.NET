using Aggregates.Contracts;
using StructureMap.Pipeline;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace Aggregates.Internal
{
    // somewhat from https://github.com/Particular/NServiceBus.StructureMap/blob/master/src/NServiceBus.StructureMap/StructureMapObjectBuilder.cs
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
            _container.Configure(x =>
            {
                x.For(concrete).Use(concrete).SetLifecycleTo(ConvertLifestyle(lifestyle));

                foreach (var implementedInterface in GetAllInterfacesImplementedBy(concrete))
                {
                    x.For(implementedInterface).Use(c => c.GetInstance(concrete));
                }
            });
        }
        public void Register(Type serviceType, object instance, Contracts.Lifestyle lifestyle)
        {
            _container.Configure(x =>
            {
                x.For(serviceType).Use(instance).SetLifecycleTo(ConvertLifestyle(lifestyle));

                foreach (var implementedInterface in GetAllInterfacesImplementedBy(serviceType))
                {
                    x.For(implementedInterface).Use(c => c.GetInstance(serviceType));
                }
            });
        }
        public void Register<TInterface>(TInterface instance, Contracts.Lifestyle lifestyle)
        {
            _container.Configure(x =>
            {
                x.For<TInterface>().Use(() => instance).SetLifecycleTo(ConvertLifestyle(lifestyle));

                foreach (var implementedInterface in GetAllInterfacesImplementedBy(typeof(TInterface)))
                {
                    x.For(implementedInterface).Use(c => c.GetInstance<TInterface>());
                }
            });
        }

        public void Register<TInterface>(Func<IContainer, TInterface> factory, Contracts.Lifestyle lifestyle, string name = null)
        {

            _container.Configure(x =>
            {
                var use = x.For<TInterface>().Use(y => factory(this));
                if (!string.IsNullOrEmpty(name))
                    use.Named(name);
                use.SetLifecycleTo(ConvertLifestyle(lifestyle));

                foreach (var implementedInterface in GetAllInterfacesImplementedBy(typeof(TInterface)))
                {
                    x.For(implementedInterface).Use(c => c.GetInstance<TInterface>());
                }
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
        public bool HasService(Type componentType)
        {
            return _container.Model.PluginTypes.Any(t => t.PluginType == componentType);
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
        static IEnumerable<Type> GetAllInterfacesImplementedBy(Type t)
        {
            return t.GetInterfaces().Where(x => !x.FullName.StartsWith("System."));
        }

        public IContainer GetChildContainer()
        {
            return new Container(_container.GetNestedContainer());
        }
    }
}
