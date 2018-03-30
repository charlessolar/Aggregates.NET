using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Aggregates.Internal.Cloning
{
    class ReferenceEqualityComparer : EqualityComparer<object>
    {
        public override bool Equals(object x, object y)
        {
            return ReferenceEquals(x, y);
        }

        public override int GetHashCode(object obj)
        {
            if (obj == null) return 0;
            // The RuntimeHelpers.GetHashCode method always calls the Object.GetHashCode method non-virtually, 
            // even if the object's type has overridden the Object.GetHashCode method.
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}