using System;
using System.Reflection;

namespace Aggregates.Internal.Cloning
{
    static class ObjectCloneExtensions
    {
        private static ObjectCloneManager Cloner = new ObjectCloneManager();
        /// <summary>
        ///     Determines whether this instance is primitive.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns></returns>
        public static bool IsPrimitive(this Type type)
        {
            if (type.IsValueType && type.IsPrimitive) return true;
            if (type == typeof (string)) return true;
            if (type == typeof (decimal)) return true;
            if (type == typeof (DateTime)) return true;
            return false;
        }

        /// <summary>
        ///     Gets the backing field property for a given field.
        /// </summary>
        /// <param name="fieldInfo">The field information.</param>
        /// <param name="typeToReflect">The type to reflect.</param>
        /// <param name="bindingFlags">The binding flags.</param>
        /// <returns></returns>
        public static PropertyInfo GetBackingFieldProperty(this FieldInfo fieldInfo, Type typeToReflect,
            BindingFlags bindingFlags)
        {
            return
                typeToReflect.GetProperty(
                    fieldInfo.Name.Substring(1, fieldInfo.Name.IndexOf("k__", StringComparison.Ordinal) - 2),
                    bindingFlags);
        }

        /// <summary>
        ///     Determines whether the given field is a backing field.
        /// </summary>
        /// <param name="fieldInfo">The field information.</param>
        /// <returns></returns>
        public static bool IsBackingField(this FieldInfo fieldInfo)
        {
            return fieldInfo.Name.Contains("k__BackingField");
        }

        /// <summary>
        ///     Copies the given object using a deep clone mechanism.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="original">The original.</param>
        /// <returns></returns>
        public static T Copy<T>(this T original)
        {
            return (T) Cloner.Clone(original);
        }
    }
}