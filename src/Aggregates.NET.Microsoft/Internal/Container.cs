using Aggregates.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        private readonly IServiceCollection _serviceCollection;
        private readonly IServiceProvider _provider;

        public Container(IServiceCollection serviceCollection, IServiceProvider provider)
        {
            _serviceCollection = serviceCollection;
            _provider = provider;
        }

        public void Dispose()
        {
        }

        private ServiceLifetime ConvertLifestyle(Contracts.Lifestyle lifestyle)
        {
            switch (lifestyle)
            {
                case Contracts.Lifestyle.PerInstance:
                    return ServiceLifetime.Transient;
                case Contracts.Lifestyle.Singleton:
                    return ServiceLifetime.Singleton;
                case Contracts.Lifestyle.UnitOfWork:
                    return ServiceLifetime.Scoped;
            }
            throw new ArgumentException($"Unknown lifestyle {lifestyle}");
        }

        public void Register(Type concrete, Contracts.Lifestyle lifestyle)
        {
            _serviceCollection.TryAdd(new ServiceDescriptor(concrete, ConvertLifestyle(lifestyle)));
            RegisterInterfaces(concrete);
        }
        public void Register<TInterface>(TInterface instance, Contracts.Lifestyle lifestyle) 
        {
            _serviceCollection.TryAdd(new ServiceDescriptor(instance.GetType(), (_) => instance, ConvertLifestyle(lifestyle)));
            RegisterInterfaces(typeof(TInterface));
        }
        public void Register(Type componentType, object instance, Contracts.Lifestyle lifestyle)
        {
            _serviceCollection.TryAdd(new ServiceDescriptor(componentType, (_) => instance, ConvertLifestyle(lifestyle)));
            RegisterInterfaces(componentType);
        }

        public void Register<TInterface>(Func<IContainer, TInterface> factory, Contracts.Lifestyle lifestyle, string name = null) 
        {
            // todo: support name? or remove name?

            _serviceCollection.TryAdd(new ServiceDescriptor(typeof(TInterface), (provider) => factory(this), ConvertLifestyle(lifestyle)));
            RegisterInterfaces(typeof(TInterface));
        }
        public void Register<TInterface, TConcrete>(Contracts.Lifestyle lifestyle, string name = null)
        {
            _serviceCollection.TryAdd(new ServiceDescriptor(typeof(TInterface), typeof(TConcrete), ConvertLifestyle(lifestyle)));
            RegisterInterfaces(typeof(TInterface));
        }
        public bool HasService(Type componentType)
        {
            return _serviceCollection.Any(sd => sd.ServiceType == componentType);
        }
        void RegisterInterfaces(Type component)
        {
            var interfaces = component.GetInterfaces();
            foreach (var serviceType in interfaces)
            {
                // see https://andrewlock.net/how-to-register-a-service-with-multiple-interfaces-for-in-asp-net-core-di/
                _serviceCollection.Add(new ServiceDescriptor(serviceType, sp => sp.GetService(component), ServiceLifetime.Transient));
            }
        }


        public object Resolve(Type resolve)
        {
            return _provider.GetService(resolve);
        }
        public TResolve Resolve<TResolve>() 
        {
            return _provider.GetService<TResolve>();
        }
        public IEnumerable<TResolve> ResolveAll<TResolve>()
        {
            return _provider.GetServices<TResolve>();
        }
        public IEnumerable<object> ResolveAll(Type resolveType)
        {
            return _provider.GetServices(resolveType);
        }
        public object TryResolve(Type resolve)
        {
            try
            {
                return _provider.GetService(resolve);
            }
            catch
            {
                return null;
            }
        }
        public TResolve TryResolve<TResolve>()
        {
            try
            {
                return _provider.GetService<TResolve>();
            }
            catch
            {
                return default(TResolve);
            }
        }

        public IContainer GetChildContainer()
        {
            return new ChildScope(_provider.CreateScope());
        }

        class ChildScope : IContainer, IDisposable
        {
            private readonly IServiceScope _scope;
            public ChildScope(IServiceScope scope)
            {
                _scope = scope;
            }

            public void Dispose()
            {
                _scope.Dispose();
            }

            public IContainer GetChildContainer()
            {
                throw new InvalidOperationException();
            }

            public bool HasService(Type serviceType)
            {
                return _scope.ServiceProvider.GetService(serviceType) != null;
            }
            #region registration
            public void Register(Type concrete, Lifestyle lifestyle)
            {
                throw new InvalidOperationException();
            }

            public void Register(Type concrete, object instance, Lifestyle lifestyle)
            {
                throw new InvalidOperationException();
            }

            public void Register<TInterface>(TInterface instance, Lifestyle lifestyle)
            {
                throw new InvalidOperationException();
            }

            public void Register<TInterface, TConcrete>(Lifestyle lifestyle, string name = null)
            {
                throw new InvalidOperationException();
            }

            public void Register<TInterface>(Func<IContainer, TInterface> factory, Lifestyle lifestyle, string name = null)
            {
                throw new InvalidOperationException();
            }
            #endregion
            public object Resolve(Type resolve)
            {
                return _scope.ServiceProvider.GetService(resolve);
            }

            public TResolve Resolve<TResolve>()
            {
                return _scope.ServiceProvider.GetService<TResolve>();
            }

            public IEnumerable<TResolve> ResolveAll<TResolve>()
            {
                return _scope.ServiceProvider.GetServices<TResolve>();
            }

            public IEnumerable<object> ResolveAll(Type resolve)
            {
                return _scope.ServiceProvider.GetServices(resolve);
            }

            public object TryResolve(Type resolve)
            {
                try
                {
                    return _scope.ServiceProvider.GetService(resolve);
                }
                catch
                {
                    return null;
                }
            }

            public TResolve TryResolve<TResolve>()
            {
                try
                {
                    return _scope.ServiceProvider.GetService<TResolve>();
                }
                catch
                {
                    return default(TResolve);
                }
            }
        }
    }
}
