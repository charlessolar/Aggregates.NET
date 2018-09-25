using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class, AllowMultiple = false)]
    public class Versioned : Attribute
    {
        public string Name { get; private set; }
        public int Version { get; private set; }

        public Versioned(string name, int version = 1)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));
            if (version < 1)
                throw new ArgumentOutOfRangeException(nameof(version), "Version must be > 1");

            this.Name = name;
            this.Version = version;
        }
    }
}
