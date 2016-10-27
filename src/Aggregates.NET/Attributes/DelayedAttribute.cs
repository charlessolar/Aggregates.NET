using System;

namespace Aggregates.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public class DelayedAttribute : Attribute
    {
        public DelayedAttribute(Type type, int? count = null, TimeSpan? delay = null)
        {
            Type = type;
            Count = count;
            Delay = delay;
        }

        public Type Type { get; private set; }
        public int? Count { get; private set; }
        public TimeSpan? Delay { get; private set; }
    }
}
