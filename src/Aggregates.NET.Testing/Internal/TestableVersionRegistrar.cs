using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class TestableVersionRegistrar : Contracts.IVersionRegistrar
    {
        private readonly Dictionary<string, Type> _versions = new Dictionary<string, Type>();

        public Type GetNamedType(string versionedName)
        {
            if (!_versions.TryGetValue(versionedName, out var type))
                throw new Exception($"Unknown {versionedName}");
            return type;
        }

        public string GetVersionedName(Type versionedType)
        {
            var name = $"Testing.{versionedType.FullName}";
            _versions[name] = versionedType;
            return name;
        }

        public void Load(Type[] types)
        {
            throw new NotImplementedException();
        }
    }
}
