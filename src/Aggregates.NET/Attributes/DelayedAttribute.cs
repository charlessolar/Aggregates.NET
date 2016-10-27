using System;

namespace Aggregates.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public class DelayedAttribute : Attribute
    {
        public DelayedAttribute(Type type, int Count = -1, int DelayMs = -1)
        {
            this.Type = type;
            if(Count != -1)
                this.Count = Count;
            if(DelayMs != -1)
                this.Delay = DelayMs;
        }

        public Type Type { get; private set; }
        public int? Count { get; private set; }
        public int? Delay { get; private set; }
    }
}
