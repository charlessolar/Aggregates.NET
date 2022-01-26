using System;
using System.Collections.Generic;
using System.Linq;
using global::SimpleInjector;
using System.Linq.Expressions;
using SimpleInjector.Lifestyles;
using System.Reflection;
using SimpleInjector.Advanced;

// https://github.com/WilliamBZA/NServicebus.SimpleInjector/blob/72f2b243f9c4bb6d1d4b8fc0b41946bef9fec8ed/src/NServiceBus.SimpleInjector/CollectionRegistrationExtensions.cs
namespace Aggregates.Internal
{
    public static class CollectionRegistrationExtensions
    {
        /// <summary>
        /// Adds support to the container to resolve arrays and lists
        /// </summary>
        /// <param name="container"></param>
        public static void AllowToResolveArraysAndLists(this global::SimpleInjector.Container container)
        {
            container.ResolveUnregisteredType += (sender, e) => {
                var serviceType = e.UnregisteredServiceType;

                if (serviceType.IsArray)
                {
                    RegisterArrayResolver(e, container,
                        serviceType.GetElementType());
                }
                else if (serviceType.IsGenericType &&
                  serviceType.GetGenericTypeDefinition() == typeof(IList<>))
                {
                    RegisterArrayResolver(e, container,
                        serviceType.GetGenericArguments()[0]);
                }
            };
        }

        public static global::SimpleInjector.Container Clone(this global::SimpleInjector.Container parentContainer)
        {
            var clonedContainer = new global::SimpleInjector.Container();
            clonedContainer.AllowToResolveArraysAndLists();
            clonedContainer.Options.AllowOverridingRegistrations = true;
            clonedContainer.Options.DefaultScopedLifestyle = new AsyncScopedLifestyle();
            clonedContainer.Options.AutoWirePropertiesImplicitly();
            AsyncScopedLifestyle.BeginScope(clonedContainer);
            foreach (var reg in parentContainer.GetCurrentRegistrations())
            {
                if (reg.Lifestyle == Lifestyle.Singleton && !HasComponent(clonedContainer, reg.ServiceType))
                {
                    clonedContainer.Register(reg.ServiceType, reg.GetInstance, reg.Lifestyle);
                }
                else
                {
                    var registration = RegistrationOptions(reg, clonedContainer).First(r => r != null);
                    clonedContainer.AddRegistration(reg.ServiceType, registration);
                }
            }
            return clonedContainer;
        }

        static IEnumerable<Registration> RegistrationOptions(InstanceProducer registrationToCopy, global::SimpleInjector.Container container)
        {
            yield return CreateRegistrationFromPrivateField(registrationToCopy, container, "instanceCreator");
            yield return CreateRegistrationFromPrivateField(registrationToCopy, container, "userSuppliedInstanceCreator");
            yield return registrationToCopy.Lifestyle.CreateRegistration(registrationToCopy.ServiceType, container);
        }

        static object GetPrivateField(object obj, string fieldName)
        {
            var field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.Instance);
            var fieldValue = field?.GetValue(obj);
            return fieldValue;
        }

        static Registration CreateRegistrationFromPrivateField(InstanceProducer instanceProducer, global::SimpleInjector.Container container, string privateFieldName)
        {
            var instanceCreator = (Func<object>)GetPrivateField(instanceProducer.Registration, privateFieldName);
            if (instanceCreator != null)
            {
                return instanceProducer.Lifestyle.CreateRegistration(instanceProducer.ServiceType, instanceCreator, container);
            }
            return null;
        }

        static bool HasComponent(global::SimpleInjector.Container container, Type componentType)
        {
            return container.GetCurrentRegistrations().Any(r => r.ServiceType == componentType);
        }

        private static void RegisterArrayResolver(UnregisteredTypeEventArgs e,
            global::SimpleInjector.Container container, Type elementType)
        {
            var producer = container.GetRegistration(typeof(IEnumerable<>)
                .MakeGenericType(elementType));
            var enumerableExpression = producer.BuildExpression();
            var arrayMethod = typeof(Enumerable).GetMethod("ToArray")
                .MakeGenericMethod(elementType);
            var arrayExpression =
                Expression.Call(arrayMethod, enumerableExpression);

            e.Register(arrayExpression);
        }
    }
    public static class ImplicitPropertyInjectionExtensions
    {
        /// <summary>
        /// Configures the options object's PropertySelectionBehaviour to use Implicit property injection
        /// </summary>
        /// <param name="options"></param>
        public static void AutoWirePropertiesImplicitly(this ContainerOptions options)
        {
            options.PropertySelectionBehavior = new ImplicitPropertyInjectionBehavior(options.PropertySelectionBehavior, options);
        }

        internal sealed class ImplicitPropertyInjectionBehavior
            : IPropertySelectionBehavior
        {
            private readonly IPropertySelectionBehavior core;
            private readonly ContainerOptions options;

            internal ImplicitPropertyInjectionBehavior(IPropertySelectionBehavior core, ContainerOptions options)
            {
                this.core = core;
                this.options = options;
            }

            public bool SelectProperty(Type type, PropertyInfo property)
            {
                return  (IsImplicitInjectable(property) || core.SelectProperty(type, property));
            }

            private bool IsImplicitInjectable(PropertyInfo property)
            {
                return IsInjectableProperty(property) && IsAvailableService(property.PropertyType);
            }

            private static bool IsInjectableProperty(PropertyInfo property)
            {
                var setMethod = property.GetSetMethod(nonPublic: false);

                return setMethod != null && !setMethod.IsStatic && property.CanWrite;
            }

            private bool IsAvailableService(Type serviceType)
            {
                return options.Container.GetRegistration(serviceType) != null;
            }
        }
    }
}
