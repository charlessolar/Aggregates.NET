using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Xml.Linq;

namespace Aggregates.Internal.Cloning
{
    /// <summary>
    ///     Handles deep cloning/shallow cloning of objects
    ///     from https://github.com/chivandikwa/net-object-deep-copy and modified
    /// </summary>
    class ObjectCloneManager
    {
        #region Fields

        private Func<object, object> cloneMethod;
        private readonly Dictionary<Type, FieldInfo[]> fieldsRequiringDeepClone;

        #endregion

        #region Ctors

        public ObjectCloneManager()
        {
            this.fieldsRequiringDeepClone = new Dictionary<Type, FieldInfo[]>();
            this.CompileMemberwiseCloneLambdaExpression();
        }

        #endregion

        private void CompileMemberwiseCloneLambdaExpression()
        {
            MethodInfo methodInfo = typeof (object).GetMethod("MemberwiseClone",
                BindingFlags.NonPublic | BindingFlags.Instance);
            ParameterExpression parameter = Expression.Parameter(typeof (object));
            MethodCallExpression body = Expression.Call(parameter, methodInfo);
            this.cloneMethod = Expression.Lambda<Func<object, object>>(body, parameter).Compile();
        }

        public object Clone(object originalObject)
        {
            return this.ExecuteClone(originalObject, new Dictionary<object, object>(new ReferenceEqualityComparer()),
                true);
        }

        private object ExecuteClone(object originalObject, IDictionary<object, object> visited, bool checkObjectGraph)
        {
            if (originalObject == null) return null;

            Type typeToReflect = originalObject.GetType();

            if (typeToReflect.IsPrimitive())
            {
                return originalObject;
            }

            if (typeof (XElement).IsAssignableFrom(typeToReflect)) return new XElement(originalObject as XElement);

            if (checkObjectGraph && visited.ContainsKey(originalObject)) return visited[originalObject];

            if (typeof (Delegate).IsAssignableFrom(typeToReflect)) return null;
            
            object cloneObject;

            if (typeToReflect.IsArray)
            {
                Type arrayType = typeToReflect.GetElementType();
                var originalArray = (Array) originalObject;
                Array clonedArray = Array.CreateInstance(arrayType, originalArray.Length);
                cloneObject = clonedArray;

                if (checkObjectGraph) visited.Add(originalObject, cloneObject);

                if (arrayType.IsPrimitive())
                {
                    //ignore array of primitive, shallow copy will suffic
                }
                else if (typeToReflect.IsPrimitive() == false)
                {
                    clonedArray.ForEach(
                        (array, indices) =>
                            array.SetValue(
                                this.ExecuteClone(originalArray.GetValue(indices), visited, !arrayType.IsValueType),
                                indices));
                }
                else
                {
                    Array.Copy(originalArray, clonedArray, clonedArray.Length);
                }
            }
            else
            {
                cloneObject = this.cloneMethod.Invoke(originalObject);
                if (checkObjectGraph) visited.Add(originalObject, cloneObject);
            }

            this.CopyFields(originalObject, visited, cloneObject, typeToReflect);
            this.RecursiveCopyBaseTypePrivateFields(originalObject, visited, cloneObject, typeToReflect);
            return cloneObject;
        }

        private void RecursiveCopyBaseTypePrivateFields(object originalObject, IDictionary<object, object> visited,
            object cloneObject, Type typeToReflect)
        {
            if (typeToReflect.BaseType != null)
            {
                this.RecursiveCopyBaseTypePrivateFields(originalObject, visited, cloneObject, typeToReflect.BaseType);
                this.CopyFields(originalObject, visited, cloneObject, typeToReflect.BaseType,
                    BindingFlags.Instance | BindingFlags.NonPublic, info => info.IsPrivate);
            }
        }

        private FieldInfo[] CachedFieldsRequiringDeepClone(Type typeToReflect, object cloneObject)
        {
            FieldInfo[] result;

            if (!this.fieldsRequiringDeepClone.TryGetValue(typeToReflect, out result))
            {
                result = this.FieldsRequiringDeepClone(typeToReflect, cloneObject).ToArray();
                this.fieldsRequiringDeepClone[typeToReflect] = result;
            }

            return result;
        }

        private IEnumerable<FieldInfo> FieldsRequiringDeepClone(Type typeToReflect, object cloneObject)
        {
            foreach (
                FieldInfo fieldInfo in
                    typeToReflect.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public |
                                            BindingFlags.FlattenHierarchy))
            {
                yield return fieldInfo;
            }

            while (typeToReflect.BaseType != null)
            {
                typeToReflect = typeToReflect.BaseType;

                foreach (FieldInfo fieldInfo in typeToReflect.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    if (!fieldInfo.IsPrivate) continue;
                    yield return fieldInfo;
                }
            }
        }
        

        private void CopyFields(object originalObject, IDictionary<object, object> visited, object cloneObject,
            Type typeToReflect,
            BindingFlags bindingFlags =
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy,
            Func<FieldInfo, bool> filter = null)
        {
            foreach (FieldInfo fieldInfo in this.CachedFieldsRequiringDeepClone(typeToReflect, cloneObject))
            {
                if (filter != null && filter(fieldInfo) == false) continue;
                if (fieldInfo.FieldType.IsPrimitive())
                {
                    continue;
                }
                
                object originalFieldValue = fieldInfo.GetValue(originalObject);
                object clonedFieldValue = this.ExecuteClone(originalFieldValue, visited,
                    !fieldInfo.FieldType.IsValueType);
                fieldInfo.SetValue(cloneObject, clonedFieldValue);
            }
        }
    }
}