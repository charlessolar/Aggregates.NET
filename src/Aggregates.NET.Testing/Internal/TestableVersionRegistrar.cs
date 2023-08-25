using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Aggregates.Internal
{
    class TestableVersionRegistrar : Contracts.IVersionRegistrar
    {
        private static readonly Dictionary<string, Type> Versions = new Dictionary<string, Type>();

        public Type GetNamedType(string versionedName)
        {
            if (!Versions.TryGetValue(versionedName, out var type))
                throw new Exception($"Unknown {versionedName}");
            return type;
        }

        public string GetVersionedName(Type versionedType, bool insert = true)
        {
            var name = $"Testing.{versionedType.FullName}";
            Versions[name] = versionedType;
            return name;
        }

        public void Load(Type[] types)
        {
            throw new NotImplementedException();
        }
    }
}
