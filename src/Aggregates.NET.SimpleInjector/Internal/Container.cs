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
        public void Register<TInterface>(TInterface instance, Contracts.Lifestyle lifestyle) where TInterface : class
        {
            if (_child) return;
            _container.Register<TInterface>(() => instance, ConvertLifestyle(lifestyle));
        }

        public void Register<TInterface>(Func<IContainer, TInterface> factory, Contracts.Lifestyle lifestyle, string name = null) where TInterface : class
        {
            if (_child) return;

            // Trick to accomplish what named instances are meant to - registering multiple of the same interface.
            if (!string.IsNullOrEmpty(name))
            {
                if (!_namedCollections.ContainsKey(typeof(TInterface)))
                    _namedCollections[typeof(TInterface)] = new List<SimpleInjector.Registration>();

                _namedCollections[typeof(TInterface)].Add(ConvertLifestyle(lifestyle).CreateRegistration<TInterface>(() => factory(this), _container));

                _container.Collection.Register<TInterface>(_namedCollections[typeof(TInterface)]);
                return;
            }
            _container.Register(() => factory(this), ConvertLifestyle(lifestyle));
        }
        public void Register<TInterface, TConcrete>(Contracts.Lifestyle lifestyle, string name = null) where TInterface : class where TConcrete : class, TInterface
        {
            if (_child) return;

            // Trick to accomplish what named instances are meant to - registering multiple of the same interface.
            if (!string.IsNullOrEmpty(name))
            {
                if (!_namedCollections.ContainsKey(typeof(TInterface)))
                    _namedCollections[typeof(TInterface)] = new List<SimpleInjector.Registration>();

                _namedCollections[typeof(TInterface)].Add(ConvertLifestyle(lifestyle).CreateRegistration<TConcrete>( _container));

                _container.Collection.Register<TInterface>(_namedCollections[typeof(TInterface)]);
                return;
            }
            _container.Register<TInterface, TConcrete>(ConvertLifestyle(lifestyle));
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
            try
            {
                return _container.GetInstance(resolve);
            }
            catch (ActivationException)
            {
                return null;
            }
        }
        public TResolve TryResolve<TResolve>() where TResolve : class
        {
            try
            {
                return _container.GetInstance<TResolve>();
            }
            catch (ActivationException)
            {
                return null;
            }
        }

        public IContainer GetChildContainer()
        {
            return new Container(_container, child: true);
        }
    }
}
