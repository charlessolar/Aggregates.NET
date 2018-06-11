using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace Aggregates.Attributes
{
    public enum DeliveryMode
    {
        /// <summary>
        /// Deliver each message as a single call to message handler
        /// </summary>
        Single,
        /// <summary>
        /// Deliver only the first message from a collection of waiting messages
        /// </summary>
        First,
        /// <summary>
        /// Deliver only the last message from a collection of waiting messages
        /// </summary>
        Last,
        /// <summary>
        /// Deliver both the first and last message from a collection of waiting messages
        /// </summary>
        FirstAndLast
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public class DelayedAttribute : Attribute
    {
        public DelayedAttribute(Type type, int count = -1, int delayMs = -1, DeliveryMode mode = DeliveryMode.Single, bool useKeyProperties = true)
        {
            this.Type = type;
            if(count != -1)
                this.Count = count;
            if(delayMs != -1)
                this.Delay = delayMs;
            this.Mode = mode;

            if (Count > 10000)
                throw new ArgumentException($"{nameof(Count)} too large - maximum is 10000");

            if (!this.Count.HasValue && !this.Delay.HasValue)
                throw new ArgumentException($"{nameof(Count)} or {nameof(delayMs)} is required to use Delayed attribute");
            
            
            var keys =
                type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(x => new Tuple<PropertyInfo, KeyProperty>(x, x.GetCustomAttribute<KeyProperty>(false)));
            
            // Slightly complex logic to select properties having `KeyProperty` attributes to combine into a string.
            // if `useKeyProperties` is false, only use `KeyProperty` attributes having `Always` be true
            if (keys.Any(x => x.Item2 != null && (useKeyProperties || x.Item2.Always)))
            {
                this.KeyPropertyFunc =
                    (o) =>
                    {
                        if (o.GetType() != this.Type)
                            throw new ArgumentException($"Incorrect type - {this.Type.FullName} expected, {o.GetType().FullName} given");

                        return keys.Where(x => x.Item2 != null && (useKeyProperties || x.Item2.Always)).Select(x => x.Item1.GetValue(o).ToString()).Aggregate((cur, next) => $"{cur}:{next}");
                    };
            }
            else
            {
                this.KeyPropertyFunc = (_) => "";
            }
            
        }

        public Type Type { get; private set; }
        public int? Count { get; private set; }
        public int? Delay { get; private set; }
        public DeliveryMode? Mode { get; private set; }
        public Func<object, string> KeyPropertyFunc { get; private set; }
    }
    
}
