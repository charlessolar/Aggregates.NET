using Aggregates.Contracts;
using StructureMap.Pipeline;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
            lock (configuredInstances)
            {
                if (configuredInstances.ContainsKey(concrete))
                {
                    return;
                }
            }
            ConfiguredInstance configuredInstance = null;
            _container.Configure(x =>
            {
                configuredInstance = x.For(concrete).Use(concrete);
                configuredInstance.SetLifecycleTo(ConvertLifestyle(lifestyle));

                foreach (var implementedInterface in GetAllInterfacesImplementedBy(concrete))
                {
                    x.For(implementedInterface).Use(c => c.GetInstance(concrete));
                }
            });
            lock (configuredInstances)
            {
                configuredInstances.Add(concrete, configuredInstance);
            }
        }
        public void Register(Type serviceType, object instance, Contracts.Lifestyle lifestyle)
        {
            lock (configuredInstances)
            {
                if (configuredInstances.ContainsKey(serviceType))
                {
                    return;
                }
            }
            ObjectInstance configuredInstance = null;
            _container.Configure(x =>
            {
                configuredInstance = x.For(serviceType).Use(instance);
                configuredInstance.SetLifecycleTo(ConvertLifestyle(lifestyle));

                foreach (var implementedInterface in GetAllInterfacesImplementedBy(serviceType))
                {
                    x.For(implementedInterface).Use(c => c.GetInstance(serviceType));
                }
            });
            lock (configuredInstances)
            {
                configuredInstances.Add(serviceType, configuredInstance);
            }
        }
        public void Register<TInterface>(TInterface instance, Contracts.Lifestyle lifestyle)
        {
            lock (configuredInstances)
            {
                if (configuredInstances.ContainsKey(typeof(TInterface)))
                {
                    return;
                }
            }
            LambdaInstance<TInterface, TInterface> configuredInstance = null;
            _container.Configure(x =>
            {
                configuredInstance = x.For<TInterface>().Use(() => instance);
                configuredInstance.SetLifecycleTo(ConvertLifestyle(lifestyle));

                foreach (var implementedInterface in GetAllInterfacesImplementedBy(typeof(TInterface)))
                {
                    x.For(implementedInterface).Use(c => c.GetInstance<TInterface>());
                }
            });
            lock (configuredInstances)
            {
                configuredInstances.Add(typeof(TInterface), configuredInstance);
            }
        }

        public void Register<TInterface>(Func<IContainer, TInterface> factory, Contracts.Lifestyle lifestyle, string name = null)
        {
            lock (configuredInstances)
            {
                if (configuredInstances.ContainsKey(typeof(TInterface)))
                {
                    return;
                }
            }


            LambdaInstance<TInterface, TInterface> configuredInstance = null;
            _container.Configure(x =>
            {
                var use = configuredInstance = x.For<TInterface>().Use(y => factory(this));
                if (!string.IsNullOrEmpty(name))
                    use.Named(name);
                use.SetLifecycleTo(ConvertLifestyle(lifestyle));

                foreach (var implementedInterface in GetAllInterfacesImplementedBy(typeof(TInterface)))
                {
                    x.For(implementedInterface).Use(c => c.GetInstance<TInterface>());
                }
            });
            lock (configuredInstances)
            {
                configuredInstances.Add(typeof(TInterface), configuredInstance);
            }
        }
        public void Register<TInterface, TConcrete>(Contracts.Lifestyle lifestyle, string name = null)
        {
            lock (configuredInstances)
            {
                if (configuredInstances.ContainsKey(typeof(TInterface)))
                {
                    return;
                }
            }
            ConfiguredInstance configuredInstance = null;
            _container.Configure(x =>
            {
                var use = configuredInstance = x.For(typeof(TInterface)).Use(typeof(TConcrete));
                if (!string.IsNullOrEmpty(name))
                    use.Named(name);
                use.SetLifecycleTo(ConvertLifestyle(lifestyle));
            });
            lock (configuredInstances)
            {
                configuredInstances.Add(typeof(TInterface), configuredInstance);
            }
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
        Dictionary<Type, Instance> configuredInstances = new Dictionary<Type, Instance>();
    }
}
