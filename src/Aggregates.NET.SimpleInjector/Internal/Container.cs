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
    [ExcludeFromCodeCoverage]
    class Container : IContainer, IDisposable
    {
        private readonly SimpleInjector.Container _container;
        private readonly Dictionary<Type, List<SimpleInjector.Registration>> _namedCollections;
        private readonly bool _child;

        public Container(SimpleInjector.Container container, bool child=false)
        {
            _container = container;
            _child = child;
            _namedCollections = new Dictionary<Type, List<SimpleInjector.Registration>>();

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
            _container.Register(concrete, concrete, ConvertLifestyle(lifestyle));
        }
        public void Register(Type service, object instance, Contracts.Lifestyle lifestyle)
        {
            if (_child) return;
            _container.Register(service, () => instance, ConvertLifestyle(lifestyle));
        }
        public void Register<TInterface>(TInterface instance, Contracts.Lifestyle lifestyle)
        {
            if (_child) return;
            _container.Register(instance.GetType(), () => instance, ConvertLifestyle(lifestyle));
            //_container.Register<TInterface>(() => instance, ConvertLifestyle(lifestyle));
        }

        public void Register<TInterface>(Func<IContainer, TInterface> factory, Contracts.Lifestyle lifestyle, string name = null) 
        {
            if (_child) return;

            // Trick to accomplish what named instances are meant to - registering multiple of the same interface.
            if (!string.IsNullOrEmpty(name))
            {
                if (!_namedCollections.ContainsKey(typeof(TInterface)))
                    _namedCollections[typeof(TInterface)] = new List<SimpleInjector.Registration>();

                _namedCollections[typeof(TInterface)].Add(ConvertLifestyle(lifestyle).CreateRegistration(typeof(TInterface), () => factory(this), _container));

                _container.Collection.Register(typeof(TInterface), _namedCollections[typeof(TInterface)]);
                return;
            }
            _container.Register(typeof(TInterface), () => factory(this), ConvertLifestyle(lifestyle));
        }
        public void Register<TInterface, TConcrete>(Contracts.Lifestyle lifestyle, string name = null) 
        {
            if (_child) return;

            // Trick to accomplish what named instances are meant to - registering multiple of the same interface.
            if (!string.IsNullOrEmpty(name))
            {
                if (!_namedCollections.ContainsKey(typeof(TInterface)))
                    _namedCollections[typeof(TInterface)] = new List<SimpleInjector.Registration>();

                _namedCollections[typeof(TInterface)].Add(ConvertLifestyle(lifestyle).CreateRegistration(typeof(TConcrete), _container));

                _container.Collection.Register(typeof(TInterface), _namedCollections[typeof(TInterface)]);
                return;
            }
            _container.Register(typeof(TInterface), typeof(TConcrete), ConvertLifestyle(lifestyle));
        }
        public bool HasService(Type service)
        {
            return _container.GetCurrentRegistrations().Any(x => x.ServiceType == service);
        }

        public object Resolve(Type resolve)
        {
            if (!HasComponent(resolve))
            {
                throw new ActivationException("The requested type is not registered yet");
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
        public IContainer GetChildContainer()
        {
            return new Container(_container, child: true);
        }
    }
}
