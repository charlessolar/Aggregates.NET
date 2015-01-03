using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml.Linq;

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
        private Dictionary<AnonTypeId, Type> anonymousTypes = new Dictionary<AnonTypeId, Type>();
        private ModuleBuilder moduleBuilder;
        private int anonymousTypeIndex = 0;

        //vsadov: hack to force loading of VB runtime.
       // private int vb_hack = Microsoft.VisualBasic.CompilerServices.Operators.CompareString("qq","qq",true);


        public ExpressionSerializationTypeResolver()
        {
            AssemblyName asmname = new AssemblyName();
            asmname.Name = "AnonymousTypes";
            AssemblyBuilder assemblyBuilder = System.Threading.Thread.GetDomain().DefineDynamicAssembly(asmname, AssemblyBuilderAccess.Run);//RunAndSave);
            moduleBuilder = assemblyBuilder.DefineDynamicModule("AnonymousTypes");
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
                return this.GetType(typeName.Substring(0, typeName.Length - 2)).MakeArrayType();

						var assemblies = (Assembly[])typeof(AppDomain).GetMethod("GetAssemblies").Invoke(AppDomain.CurrentDomain, null);
            //// First - try all loaded types
						foreach (Assembly assembly in assemblies)
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
                NameTypePair other = obj as NameTypePair;
                return Name.Equals(other.Name) && Type.Equals(other.Type);
            }
        }

        private class AnonTypeId
        {
            public string Name { get; private set; }
            public IEnumerable<NameTypePair> Properties { get; private set; }

            public AnonTypeId(string name, IEnumerable<NameTypePair> properties)
            {
                this.Name = name;
                this.Properties = properties;
            }

            public override int GetHashCode()
            {
                int result = Name.GetHashCode();
                foreach (var ntpair in Properties)
                    result += ntpair.GetHashCode();
                return result;
            }

            public override bool Equals(object obj)
            {
                if (!(obj is AnonTypeId))
                    return false;
                AnonTypeId other = obj as AnonTypeId;
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
                    MethodInfo realMethod = method;
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
                    continue;
                }
            }
            return null;
        }

        private bool MatchPiecewise<T>(IEnumerable<T> first, IEnumerable<T> second)
        {
            T[] firstArray = first.ToArray();
            T[] secondArray = second.ToArray();
            if (firstArray.Length != secondArray.Length)
                return false;
            for (int i = 0; i < firstArray.Length; i++)
                if (!firstArray[i].Equals(secondArray[i]))
                    return false;
            return true;
        }

        //vsadov: need to take ctor parameters too as they do not 
        //necessarily match properties order as returned by GetProperties
        public Type GetOrCreateAnonymousTypeFor(string name, NameTypePair[] properties, NameTypePair[] ctr_params)
        {
            AnonTypeId id = new AnonTypeId(name, properties.Concat(ctr_params));
            if (anonymousTypes.ContainsKey(id))
                return anonymousTypes[id];

            //vsadov: VB anon type. not necessary, just looks better
            string anon_prefix = name.StartsWith("<>") ? "<>f__AnonymousType" : "VB$AnonymousType_";
            TypeBuilder anonTypeBuilder = moduleBuilder.DefineType(anon_prefix + anonymousTypeIndex++, TypeAttributes.Public | TypeAttributes.Class);

            FieldBuilder[] fieldBuilders = new FieldBuilder[properties.Length];
            PropertyBuilder[] propertyBuilders = new PropertyBuilder[properties.Length];

            for (int i = 0; i < properties.Length; i++)
            {
                fieldBuilders[i] = anonTypeBuilder.DefineField("_generatedfield_" + properties[i].Name, properties[i].Type, FieldAttributes.Private);
                propertyBuilders[i] = anonTypeBuilder.DefineProperty(properties[i].Name, PropertyAttributes.None, properties[i].Type, new Type[0]);
                MethodBuilder propertyGetterBuilder = anonTypeBuilder.DefineMethod("get_" + properties[i].Name, MethodAttributes.Public, properties[i].Type, new Type[0]);
                ILGenerator getterILGenerator = propertyGetterBuilder.GetILGenerator();
                getterILGenerator.Emit(OpCodes.Ldarg_0);
                getterILGenerator.Emit(OpCodes.Ldfld, fieldBuilders[i]);
                getterILGenerator.Emit(OpCodes.Ret);
                propertyBuilders[i].SetGetMethod(propertyGetterBuilder);
            }

            ConstructorBuilder constructorBuilder = anonTypeBuilder.DefineConstructor(MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.Public, CallingConventions.Standard, ctr_params.Select(prop => prop.Type).ToArray());
            ILGenerator constructorILGenerator = constructorBuilder.GetILGenerator();
            for (int i = 0; i < ctr_params.Length; i++)
            {
                constructorILGenerator.Emit(OpCodes.Ldarg_0);
                constructorILGenerator.Emit(OpCodes.Ldarg, i + 1);
                constructorILGenerator.Emit(OpCodes.Stfld, fieldBuilders[i]);
                constructorBuilder.DefineParameter(i + 1, ParameterAttributes.None, ctr_params[i].Name);
            }
            constructorILGenerator.Emit(OpCodes.Ret);

            //TODO - Define ToString() and GetHashCode implementations for our generated Anonymous Types
            //MethodBuilder toStringBuilder = anonTypeBuilder.DefineMethod();
            //MethodBuilder getHashCodeBuilder = anonTypeBuilder.DefineMethod();

            Type anonType = anonTypeBuilder.CreateType();
            anonymousTypes.Add(id, anonType);
            return anonType;
        }

    }
}