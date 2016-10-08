using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Xml.Linq;
// ReSharper disable All

namespace Aggregates.Specifications.Expressions
{

    //
    // This code is taken from the codeplex project Jammer.NET
    // you can find the project under http://jmr.codeplex.com/
    // Here is the URL to the original code: https://jmr.svn.codeplex.com/svn/trunk/Jmr.Silverlight/Serialization/ExpressionSerializationTypeResolver.cs
    //

    public abstract class CustomExpressionXmlConverter
    {
        public abstract Expression Deserialize(XElement expressionXml);
        public abstract XElement Serialize(Expression expression);
    }

    public class ExpressionSerializationTypeResolver
    {
        private readonly Dictionary<AnonTypeId, Type> _anonymousTypes = new Dictionary<AnonTypeId, Type>();
        private readonly ModuleBuilder _moduleBuilder;
        private int _anonymousTypeIndex;

        //vsadov: hack to force loading of VB runtime.
       // private int vb_hack = Microsoft.VisualBasic.CompilerServices.Operators.CompareString("qq","qq",true);


        public ExpressionSerializationTypeResolver()
        {
            var asmname = new AssemblyName();
            asmname.Name = "AnonymousTypes";
            var assemblyBuilder = Thread.GetDomain().DefineDynamicAssembly(asmname, AssemblyBuilderAccess.Run);//RunAndSave);
            _moduleBuilder = assemblyBuilder.DefineDynamicModule("AnonymousTypes");
        }

        protected virtual Type ResolveTypeFromString(string typeString) { return null; }
        protected virtual string ResolveStringFromType(Type type) { return null; }

        public Type GetType(string typeName, IEnumerable<Type> genericArgumentTypes)
        {
            return GetType(typeName).MakeGenericType(genericArgumentTypes.ToArray());
        }

        public Type GetType(string typeName)
        {
            Type type;
            if (string.IsNullOrEmpty(typeName))
                throw new ArgumentNullException("typeName");

            // First - try all replacers
            type = ResolveTypeFromString(typeName);
            //type = typeReplacers.Select(f => f(typeName)).FirstOrDefault();
            if (type != null)
                return type;

            // If it's an array name - get the element type and wrap in the array type.
            if (typeName.EndsWith("[]"))
                return GetType(typeName.Substring(0, typeName.Length - 2)).MakeArrayType();

						var assemblies = (Assembly[])typeof(AppDomain).GetMethod("GetAssemblies").Invoke(AppDomain.CurrentDomain, null);
            //// First - try all loaded types
						foreach (var assembly in assemblies)
            {
                type = assembly.GetType(typeName, false);//, true);
                if (type != null)
                    return type;
            }

            // Second - try just plain old Type.GetType()
            type = Type.GetType(typeName, false, true);
            if (type != null)
                return type;

            throw new ArgumentException("Could not find a matching type", typeName);
        }

        public class NameTypePair
        {
            public string Name { get; set; }
            public Type Type { get; set; }

            public override int GetHashCode()
            {
                return Name.GetHashCode() + Type.GetHashCode();
            }
            public override bool Equals(object obj)
            {
                if (!(obj is NameTypePair))
                    return false;
                var other = obj as NameTypePair;
                return Name.Equals(other.Name) && Type.Equals(other.Type);
            }
        }

        private class AnonTypeId
        {
            public string Name { get; private set; }
            public IEnumerable<NameTypePair> Properties { get; private set; }

            public AnonTypeId(string name, IEnumerable<NameTypePair> properties)
            {
                Name = name;
                Properties = properties;
            }

            public override int GetHashCode()
            {
                var result = Name.GetHashCode();
                foreach (var ntpair in Properties)
                    result += ntpair.GetHashCode();
                return result;
            }

            public override bool Equals(object obj)
            {
                if (!(obj is AnonTypeId))
                    return false;
                var other = obj as AnonTypeId;
                return (Name.Equals(other.Name)
                    && Properties.SequenceEqual(other.Properties));

            }

        }


        public MethodInfo GetMethod(Type declaringType, string name, Type[] parameterTypes, Type[] genArgTypes)
        {
            var methods = from mi in declaringType.GetMethods()
                          where mi.Name == name
                          select mi;
            foreach (var method in methods)
            {
                // Would be nice to remvoe the try/catch
                try
                {
                    var realMethod = method;
                    if (method.IsGenericMethod)
                    {
                        realMethod = method.MakeGenericMethod(genArgTypes);
                    }
                    var methodParameterTypes = realMethod.GetParameters().Select(p => p.ParameterType);
                    if (MatchPiecewise(parameterTypes, methodParameterTypes))
                    {
                        return realMethod;
                    }
                }
                catch (ArgumentException)
                {
                }
            }
            return null;
        }

        private bool MatchPiecewise<T>(IEnumerable<T> first, IEnumerable<T> second)
        {
            var firstArray = first.ToArray();
            var secondArray = second.ToArray();
            if (firstArray.Length != secondArray.Length)
                return false;
            for (var i = 0; i < firstArray.Length; i++)
                if (!firstArray[i].Equals(secondArray[i]))
                    return false;
            return true;
        }

        //vsadov: need to take ctor parameters too as they do not 
        //necessarily match properties order as returned by GetProperties
        public Type GetOrCreateAnonymousTypeFor(string name, NameTypePair[] properties, NameTypePair[] ctrParams)
        {
            var id = new AnonTypeId(name, properties.Concat(ctrParams));
            if (_anonymousTypes.ContainsKey(id))
                return _anonymousTypes[id];

            //vsadov: VB anon type. not necessary, just looks better
            var anonPrefix = name.StartsWith("<>") ? "<>f__AnonymousType" : "VB$AnonymousType_";
            var anonTypeBuilder = _moduleBuilder.DefineType(anonPrefix + _anonymousTypeIndex++, TypeAttributes.Public | TypeAttributes.Class);

            var fieldBuilders = new FieldBuilder[properties.Length];
            var propertyBuilders = new PropertyBuilder[properties.Length];

            for (var i = 0; i < properties.Length; i++)
            {
                fieldBuilders[i] = anonTypeBuilder.DefineField("_generatedfield_" + properties[i].Name, properties[i].Type, FieldAttributes.Private);
                propertyBuilders[i] = anonTypeBuilder.DefineProperty(properties[i].Name, PropertyAttributes.None, properties[i].Type, new Type[0]);
                var propertyGetterBuilder = anonTypeBuilder.DefineMethod("get_" + properties[i].Name, MethodAttributes.Public, properties[i].Type, new Type[0]);
                var getterIlGenerator = propertyGetterBuilder.GetILGenerator();
                getterIlGenerator.Emit(OpCodes.Ldarg_0);
                getterIlGenerator.Emit(OpCodes.Ldfld, fieldBuilders[i]);
                getterIlGenerator.Emit(OpCodes.Ret);
                propertyBuilders[i].SetGetMethod(propertyGetterBuilder);
            }

            var constructorBuilder = anonTypeBuilder.DefineConstructor(MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.Public, CallingConventions.Standard, ctrParams.Select(prop => prop.Type).ToArray());
            var constructorIlGenerator = constructorBuilder.GetILGenerator();
            for (var i = 0; i < ctrParams.Length; i++)
            {
                constructorIlGenerator.Emit(OpCodes.Ldarg_0);
                constructorIlGenerator.Emit(OpCodes.Ldarg, i + 1);
                constructorIlGenerator.Emit(OpCodes.Stfld, fieldBuilders[i]);
                constructorBuilder.DefineParameter(i + 1, ParameterAttributes.None, ctrParams[i].Name);
            }
            constructorIlGenerator.Emit(OpCodes.Ret);

            //TODO - Define ToString() and GetHashCode implementations for our generated Anonymous Types
            //MethodBuilder toStringBuilder = anonTypeBuilder.DefineMethod();
            //MethodBuilder getHashCodeBuilder = anonTypeBuilder.DefineMethod();

            var anonType = anonTypeBuilder.CreateType();
            _anonymousTypes.Add(id, anonType);
            return anonType;
        }

    }
}