using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using SimpleInjector.Lifestyles;

namespace Aggregates.Internal
{
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
        }

        public void Dispose()
        {
            var scope = SimpleInjector.Lifestyle.Scoped.GetCurrentScope(_container);
            scope?.Dispose();
        }

        // Note child checks are to stop the container from doing a new registration 
        // Some containers (structuremap) when you make a child container you have to do a new registration
        // Simpleinjector doesn't need or allow that

        public void RegisterSingleton<TInterface>(TInterface instance, string name = null) where TInterface : class
        {
            if (_child) return;

            // Trick to accomplish what named instances are meant to - registering multiple of the same interface.
            if(!string.IsNullOrEmpty(name))
            {
                if (!_namedCollections.ContainsKey(typeof(TInterface)))
                    _namedCollections[typeof(TInterface)] = new List<SimpleInjector.Registration> ();

                _namedCollections[typeof(TInterface)].Add(SimpleInjector.Lifestyle.Singleton.CreateRegistration<TInterface>(() => instance, _container));
                
                _container.RegisterCollection<TInterface>(_namedCollections[typeof(TInterface)]);
                return;
            }

            _container.RegisterSingleton<TInterface>(instance);
        }

        public void RegisterSingleton<TInterface>(Func<IContainer, TInterface> factory, string name = null) where TInterface : class
        {
            if (_child) return;

            // Trick to accomplish what named instances are meant to - registering multiple of the same interface.
            if (!string.IsNullOrEmpty(name))
            {
                if (!_namedCollections.ContainsKey(typeof(TInterface)))
                    _namedCollections[typeof(TInterface)] = new List<SimpleInjector.Registration>();

                _namedCollections[typeof(TInterface)].Add(SimpleInjector.Lifestyle.Singleton.CreateRegistration<TInterface>(() => factory(this), _container));

                _container.RegisterCollection<TInterface>(_namedCollections[typeof(TInterface)]);
                return;
            }
            _container.RegisterSingleton<TInterface>(() => factory(this));
        }
        public void RegisterSingleton<TInterface, TConcrete>(string name = null) where TInterface : class where TConcrete : class, TInterface
        {
            if (_child) return;

            // Trick to accomplish what named instances are meant to - registering multiple of the same interface.
            if (!string.IsNullOrEmpty(name))
            {
                if (!_namedCollections.ContainsKey(typeof(TInterface)))
                    _namedCollections[typeof(TInterface)] = new List<SimpleInjector.Registration>();

                _namedCollections[typeof(TInterface)].Add(SimpleInjector.Lifestyle.Singleton.CreateRegistration<TConcrete>( _container));

                _container.RegisterCollection<TInterface>(_namedCollections[typeof(TInterface)]);
                return;
            }
            _container.RegisterSingleton<TInterface, TConcrete>();
        }

        public void Register(Type concrete)
        {
            if (_child) return;
            _container.Register(concrete);
        }

        public void Register<TInterface>(Func<IContainer, TInterface> factory, string name = null) where TInterface : class
        {
            if (_child) return;

            // Trick to accomplish what named instances are meant to - registering multiple of the same interface.
            if (!string.IsNullOrEmpty(name))
            {
                if (!_namedCollections.ContainsKey(typeof(TInterface)))
                    _namedCollections[typeof(TInterface)] = new List<SimpleInjector.Registration>();

                _namedCollections[typeof(TInterface)].Add(SimpleInjector.Lifestyle.Singleton.CreateRegistration<TInterface>(() => factory(this), _container));

                _container.RegisterCollection<TInterface>(_namedCollections[typeof(TInterface)]);
                return;
            }
            _container.Register(() => factory(this));
        }
        public void Register<TInterface, TConcrete>(string name = null) where TInterface : class where TConcrete : class, TInterface
        {
            if (_child) return;

            // Trick to accomplish what named instances are meant to - registering multiple of the same interface.
            if (!string.IsNullOrEmpty(name))
            {
                if (!_namedCollections.ContainsKey(typeof(TInterface)))
                    _namedCollections[typeof(TInterface)] = new List<SimpleInjector.Registration>();

                _namedCollections[typeof(TInterface)].Add(SimpleInjector.Lifestyle.Singleton.CreateRegistration<TConcrete>( _container));

                _container.RegisterCollection<TInterface>(_namedCollections[typeof(TInterface)]);
                return;
            }
            _container.Register<TInterface, TConcrete>();
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
            // No support for child containers, but the scoped lifestyle can kind of due to trick
            AsyncScopedLifestyle.BeginScope(_container);
            return new Container(_container, child: true);
        }
    }
}
