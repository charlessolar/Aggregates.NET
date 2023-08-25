using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Aggregates.Internal
{
    public class IdRegistry
    {
        private int _longIdCounter = -1;
        public IReadOnlyDictionary<string, TestableId> GeneratedIds => _generatedIds;
        private readonly Dictionary<string, TestableId> _generatedIds = new Dictionary<string, TestableId>();

        public TestableId AnyId()
        {
            var generated = new Guid(0, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, (byte)-_longIdCounter });
            var id = new TestableId(Constants.GeneratedAnyId, _longIdCounter--, generated.ToString(), generated);
            if (_generatedIds.ContainsKey(id.GeneratedIdKey))
                return _generatedIds[id.GeneratedIdKey];
            return _generatedIds[id.GeneratedIdKey] = id;
        }
        public TestableId MakeId(int key)
        {
            var generated = new Guid(0, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, (byte)-_longIdCounter });
            var id = new TestableId(Constants.GeneratedNumberedId(key), _longIdCounter--, key.ToString(), generated);
            if (_generatedIds.ContainsKey(id.GeneratedIdKey))
                return _generatedIds[id.GeneratedIdKey];
            return _generatedIds[id.GeneratedIdKey] = id;
        }
        public TestableId MakeId(string key)
        {
            var generated = new Guid(0, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, (byte)-_longIdCounter });
            var id = new TestableId(Constants.GenerateNamedId(key), _longIdCounter--, key, generated);
            if (_generatedIds.ContainsKey(id.GeneratedIdKey))
                return _generatedIds[id.GeneratedIdKey];
            return _generatedIds[id.GeneratedIdKey] = id;
        }
        public TestableId MakeId(Id id)
        {
            if (id is TestableId)
                return (TestableId)id;

            // checks if the Id we get was a generated one
            var existing = _generatedIds.Values.FirstOrDefault(kv => kv.Equals(id));
            if (existing != null)
                return existing;

            var generated = new Guid(0, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, (byte)-_longIdCounter });
            var testable = new TestableId(Constants.GenerateNamedId(id.ToString()), _longIdCounter--, id.ToString(), generated);
            if (_generatedIds.ContainsKey(testable.GeneratedIdKey))
                return _generatedIds[testable.GeneratedIdKey];
            return _generatedIds[testable.GeneratedIdKey] = testable;
        }
    }
}
