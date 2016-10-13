using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public class DelayedAttribute : Attribute
    {
        public DelayedAttribute(Type type, int count = 10)
        {
            Type = type;
            Count = count;
        }

        public Type Type { get; private set; }
        public int Count { get; private set; }
    }
}
