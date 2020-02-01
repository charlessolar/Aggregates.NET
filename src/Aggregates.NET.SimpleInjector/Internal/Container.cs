using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using SimpleInjector.Lifestyles;
using SimpleInjector;
using System.Diagnostics.CodeAnalysis;

namespace Aggregates.Internal
{
    // somewhat from https://github.com/WilliamBZA/NServicebus.SimpleInjector/blob/master/src/NServiceBus.SimpleInjector/SimpleInjectorObjectBuilder.cs
    [ExcludeFromCodeCoverage]
    class Container : IContainer, IDisposable
    {
        private readonly SimpleInjector.Container _container;
        private readonly bool _child;

        public Container(SimpleInjector.Container container, bool child=false)
        {
            _container = container;
            _child = child;

            // No support for child containers, but the scoped lifestyle can kind of due to trick
            AsyncScopedLifestyle.BeginScope(_container);
        }

        private SimpleInjector.Lifestyle ConvertLifestyle(Contracts.Lifestyle lifestyle)
        {
            switch (lifestyle)
            {
                case Contracts.Lifestyle.PerInstance:
                    return SimpleInjector.Lifestyle.Transient;
                case Contracts.Lifestyle.Singleton:
                    return SimpleInjector.Lifestyle.Singleton;
                case Contracts.Lifestyle.UnitOfWork:
                    return SimpleInjector.Lifestyle.Scoped;
            }
            throw new ArgumentException($"Unknown lifestyle {lifestyle}");
        }

        public void Dispose()
        {
            var scope = SimpleInjector.Lifestyle.Scoped.GetCurrentScope(_container);
            scope?.Dispose();
        }
        

        public void Register(Type concrete, Contracts.Lifestyle lifestyle)
        {
            if (_child) return;
            
            var registration = ConvertLifestyle(lifestyle).CreateRegistration(concrete, _container);
            addRegistration(concrete, registration);
        }
        public void Register(Type service, object instance, Contracts.Lifestyle lifestyle)
        {
            if (_child) return;
            var registration = ConvertLifestyle(lifestyle).CreateRegistration(service, () => instance, _container);
            addRegistration(service, registration);
        }
        public void Register<TInterface>(TInterface instance, Contracts.Lifestyle lifestyle)
        {
            if (_child) return;
            var registration = ConvertLifestyle(lifestyle).CreateRegistration(typeof(TInterface), () => instance, _container);
            addRegistration(typeof(TInterface), registration);
        }

        public void Register<TInterface>(Func<IContainer, TInterface> factory, Contracts.Lifestyle lifestyle, string name = null)
        {
            if (_child) return;

            var registration = ConvertLifestyle(lifestyle).CreateRegistration(typeof(TInterface), () => factory(this), _container);

            addRegistration(typeof(TInterface), registration);

        }
        public void Register<TInterface, TConcrete>(Contracts.Lifestyle lifestyle, string name = null)
        {
            if (_child) return;

            var registration = ConvertLifestyle(lifestyle).CreateRegistration(typeof(TConcrete), _container);
            addRegistration(typeof(TInterface), registration);
        }
        private void addRegistration(Type serviceType, Registration registration)
        {
            if (HasComponent(serviceType))
            {
                var existingRegistrations = GetExistingRegistrationsFor(serviceType);

                _container.Collection.Register(serviceType, existingRegistrations.Union(new[] { registration }));
            }
            else
                _container.AddRegistration(serviceType, registration);

            RegisterInterfaces(serviceType, registration.Lifestyle);
        }
        private void RegisterInterfaces(Type component, SimpleInjector.Lifestyle lifestyle)
        {
            var registration = lifestyle.CreateRegistration(component, _container);

            var interfaces = component.GetInterfaces();
            foreach (var serviceType in interfaces)
                addRegistration(serviceType, registration);
        }
        public bool HasService(Type service)
        {
            return HasComponent(service);
        }

        public object Resolve(Type resolve)
        {
            if (!HasComponent(resolve))
            {
                throw new ActivationException($"The requested type {resolve.FullName} is not registered yet");
            }

            return _container.GetInstance(resolve);
        }
        public TResolve Resolve<TResolve>()
        {
            return (TResolve)Resolve(typeof(TResolve));
        }
        public IEnumerable<TResolve> ResolveAll<TResolve>()
        {
            return ResolveAll(typeof(TResolve)).Cast<TResolve>();
        }
        public IEnumerable<object> ResolveAll(Type resolve)
        {
            if (HasComponent(resolve))
            {

                try
                {
                    return _container.GetAllInstances(resolve);
                }
                catch (Exception)
                {
                    // Urgh!
                    return new[] { _container.GetInstance(resolve) };
                }
            }

            return new object[] { };
        }
        public object TryResolve(Type resolve)
        {
            try
            {
                return _container.GetInstance(resolve);
            }
            catch (ActivationException)
            {
                return null;
            }
        }
        public TResolve TryResolve<TResolve>()
        {
            try
            {
                return (TResolve)_container.GetInstance(typeof(TResolve));
            }
            catch (ActivationException)
            {
                return default(TResolve);
            }
        }
        public bool HasComponent(Type componentType)
        {
            return GetExistingRegistrationsFor(componentType).Any();
        }
        IEnumerable<Registration> GetExistingRegistrationsFor(Type implementedInterface)
        {
            return _container.GetCurrentRegistrations().Where(r => r.ServiceType == implementedInterface).Select(r => r.Registration);
        }
        Registration GetRegistrationFromDependencyLifecycle(Contracts.Lifestyle dependencyLifecycle, Type component)
        {
            return ConvertLifestyle(dependencyLifecycle).CreateRegistration(component, _container);
        }

        Registration GetRegistrationFromDependencyLifecycle(Contracts.Lifestyle dependencyLifecycle, Type component, Func<object> creator)
        {
            return ConvertLifestyle(dependencyLifecycle).CreateRegistration(component, creator, _container);
        }

        Registration GetRegistrationFromDependencyLifecycle(Contracts.Lifestyle dependencyLifecycle, Type component, object instance)
        {
            return GetRegistrationFromDependencyLifecycle(dependencyLifecycle, component, () => instance);
        }
        public IContainer GetChildContainer()
        {
            return new Container(_container, child: true);
        }
    }
}
