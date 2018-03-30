using System;
using System.Linq;
using System.Reflection;

namespace Aggregates.Internal.Cloning
{
    /// <summary>
    ///     Handles verification of two objects to ensure that they are valid clones of each other
    ///     <remarks>
    ///         IgnoreCloneAttribute and ShallowCloneAttribute
    ///         Respects
    ///     </remarks>
    /// </summary>
    static class ObjectCloneVerifier
    {
        #region Public Methods

        /// <summary>
        ///     Verifies two objects to ensure that they are valid clones of each other
        /// </summary>
        /// <param name="originalObject">The original object.</param>
        /// <param name="copyObject">The copy object.</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">Cannot verify cloning between objects of different types.</exception>
        public static bool DeepCloneIsRespected(this object originalObject, object copyObject)
        {
            if (originalObject.GetType() != copyObject.GetType())
            {
                throw new InvalidOperationException("Cannot verify cloning between objects of different types.");
            }

            return InternalVerify(originalObject, copyObject);
        }

        #endregion

        #region Private Methods

        private static bool InternalVerify(object originalObject, object copyObject)
        {
            if (originalObject == null) return copyObject == null;

            Type typeToReflect = originalObject.GetType();

            if (typeToReflect.IsPrimitive())
                return true; //A little blind here, assumption is that primitives are fine already

            if (typeof (Delegate).IsAssignableFrom(typeToReflect)) return copyObject == null;
            

            if (typeToReflect.IsArray)
            {
                Type arrayType = typeToReflect.GetElementType();
                if (arrayType.IsPrimitive() == false)
                {
                    var clonedArray = (Array) copyObject;
                    var originalArray = (Array) originalObject;

                    if (clonedArray.Length != originalArray.Length) return false;

                    for (var i = 0; i < originalArray.Length; i++)
                    {
                        bool res = InternalVerify(originalArray.GetValue(i), clonedArray.GetValue(i));
                        if (!res)
                        {
                            return false;
                        }
                    }
                }
            }

            if (ReferenceEquals(originalObject, copyObject)) return false;

            bool state = IterateFields(originalObject, copyObject, typeToReflect);
            if (!state) return false;

            state = RecursiveCopyBaseTypePrivateFields(originalObject, copyObject, typeToReflect);
            return state;
        }

        private static bool RecursiveCopyBaseTypePrivateFields(object originalObject, object cloneObject,
            Type typeToReflect)
        {
            if (typeToReflect.BaseType != null)
            {
                RecursiveCopyBaseTypePrivateFields(originalObject, cloneObject, typeToReflect.BaseType);
                return IterateFields(originalObject, cloneObject, typeToReflect.BaseType,
                    BindingFlags.Instance | BindingFlags.NonPublic, info => info.IsPrivate);
            }
            return true;
        }

        private static bool IterateFields(object originalObject, object cloneObject, Type typeToReflect,
            BindingFlags bindingFlags =
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy,
            Func<FieldInfo, bool> filter = null)
        {
            var aggregateState = true;
            foreach (FieldInfo fieldInfo in typeToReflect.GetFields(bindingFlags))
            {
                if (filter != null && filter(fieldInfo) == false) continue;
                if (fieldInfo.FieldType.IsPrimitive()) continue;

                object originalFieldValue = fieldInfo.GetValue(originalObject);
                object copyFieldValue = fieldInfo.GetValue(cloneObject);
                

                if (fieldInfo.IsBackingField())
                {
                    PropertyInfo property = fieldInfo.GetBackingFieldProperty(typeToReflect, bindingFlags);
                    
                }

                bool result = InternalVerify(originalFieldValue, copyFieldValue);

                if (!result)
                {
                    aggregateState = false;
                    break;
                }
            }
            return aggregateState;
        }

        #endregion
    }
}