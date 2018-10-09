using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class TestableVersionRegistrar : Contracts.IVersionRegistrar
    {
        public Type GetNamedType(string versionedName)
        {
            throw new NotImplementedException();
        }

        public string GetVersionedName(Type versionedType)
        {
            return $"Testing.{versionedType.FullName}";
        }

        public void Load(Type[] types)
        {
            throw new NotImplementedException();
        }
    }
}
