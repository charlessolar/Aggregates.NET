using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Internal
{
    public class TestableId : Id
    {
        public readonly string GeneratedIdKey;

        public readonly long LongId;
        public readonly string StringId;
        public readonly Guid GuidId;

        public TestableId(string generatedIdKey, long longId, string stringId, Guid guidId) : base(generatedIdKey)
        {
            GeneratedIdKey = generatedIdKey;
            LongId = longId;
            StringId = stringId;
            GuidId = guidId;
        }


        public static implicit operator long(TestableId id) => (id as TestableId).LongId;
        public static implicit operator Guid(TestableId id) => (id as TestableId).GuidId;
        public static implicit operator string(TestableId id) => (id as TestableId).StringId;
    }
}
