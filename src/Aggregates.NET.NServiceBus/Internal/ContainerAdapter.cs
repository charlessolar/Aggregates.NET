using NServiceBus.ObjectBuilder;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Aggregates.Internal
{
    public class ContainerAdapter : IConfigureComponents, IBuilder
    {
        private Contracts.IContainer Container => Configuration.Settings.LocalContainer.Value ?? Configuration.Settings.Container;


        public void ConfigureComponent(Type concreteComponent, DependencyLifecycle dependencyLifecycle)
        {
            Container.Register(concreteComponent, Map(dependencyLifecycle));
        }

        public void ConfigureComponent<T>(DependencyLifecycle dependencyLifecycle)
        {
            ConfigureComponent(typeof(T), dependencyLifecycle);
        }


        public void ConfigureComponent<T>(Func<T> componentFactory, DependencyLifecycle dependencyLifecycle) 
        {
            var componentType = typeof(T);
            Container.Register<T>((_) => componentFactory(), Map(dependencyLifecycle));
        }

        public void ConfigureComponent<T>(Func<IBuilder, T> componentFactory, DependencyLifecycle dependencyLifecycle)
        {
            Container.Register<T>((c) => componentFactory(this), Map(dependencyLifecycle));
        }

        public bool HasComponent<T>()
        {
            return HasComponent(typeof(T));
        }

        public bool HasComponent(Type componentType)
        {
            return Container.HasService(componentType);
        }

        public void RegisterSingleton(Type lookupType, object instance)
        {
            Container.Register(lookupType, instance, Contracts.Lifestyle.Singleton);
        }

        public void RegisterSingleton<T>(T instance)
        {
            RegisterSingleton(typeof(T), instance);
        }

        static Contracts.Lifestyle Map(DependencyLifecycle lifetime)
        {
            switch (lifetime)
            {
                case DependencyLifecycle.SingleInstance: return Contracts.Lifestyle.Singleton;
                case DependencyLifecycle.InstancePerCall: return Contracts.Lifestyle.PerInstance;
                case DependencyLifecycle.InstancePerUnitOfWork: return Contracts.Lifestyle.UnitOfWork;
                default: throw new NotSupportedException();
            }
        }

        public object Build(Type typeToBuild)
        {
            return Container.Resolve(typeToBuild) ?? throw new Exception($"Unable to build {typeToBuild.FullName}. Ensure the type has been registered correctly with the container.");
        }

        public T Build<T>()
        {
            return (T)Build(typeof(T));
        }

        public IEnumerable<T> BuildAll<T>()
        {
            return BuildAll(typeof(T)).Cast<T>();
        }

        public IEnumerable<object> BuildAll(Type typeToBuild)
        {
            return Container.ResolveAll(typeToBuild);
        }

        public void BuildAndDispatch(Type typeToBuild, Action<object> action)
        {
            action(Build(typeToBuild));
        }

        public IBuilder CreateChildBuilder()
        {
            return new ChildScopeAdapter(Container.GetChildContainer());
        }
        public void Dispose()
        {
        }
        public void Release(object instance)
        {
        }

        class ChildScopeAdapter : IBuilder
        {
            private readonly Contracts.IContainer _container;
            public ChildScopeAdapter(Contracts.IContainer container)
            {
                _container = container;
            }

            public object Build(Type typeToBuild)
            {
                return _container.Resolve(typeToBuild) ?? throw new Exception($"Unable to build {typeToBuild.FullName}. Ensure the type has been registered correctly with the container.");
            }

            public T Build<T>()
            {
                return (T)Build(typeof(T));
            }

            public IEnumerable<T> BuildAll<T>()
            {
                return BuildAll(typeof(T)).Cast<T>();
            }

            public IEnumerable<object> BuildAll(Type typeToBuild)
            {
                return _container.ResolveAll(typeToBuild);
            }

            public void BuildAndDispatch(Type typeToBuild, Action<object> action)
            {
                action(Build(typeToBuild));
            }

            public IBuilder CreateChildBuilder()
            {
                throw new InvalidOperationException();
            }
            public void Dispose()
            {
                _container.Dispose();
            }
            public void Release(object instance)
            {
            }
        }
    }
}
