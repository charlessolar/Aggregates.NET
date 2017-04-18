using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Aggregates
{
    public class Immutable<T> : IEquatable<T>
    {
        public readonly T Value;

        public Immutable(T value = default(T))
        {
            Value = value;
        }

        public bool HasValue => Value != null && !Value.Equals(default(T));

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            var other = (T)obj;

            return Equals(other);
        }

        public bool Equals(T other)
        {
            return Equals(new Immutable<T>(other));
        }
        public virtual bool Equals(Immutable<T> other)
        {
            return HasValue && Value.Equals(other.Value);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }


        public override string ToString()
        {
            if (!HasValue) return "";
            return Value.ToString();
        }

        
        public static implicit operator Immutable<T>(T x)
        {
            return new Immutable<T>(x);
        }
        public static bool operator ==(Immutable<T> x, Immutable<T> y)
        {
            return x.Equals(y);
        }
        public static bool operator ==(Immutable<T> x, T y)
        {
            return x.Equals(new Immutable<T>(y));
        }
        public static bool operator ==(T x, Immutable<T> y)
        {
            return y.Equals(new Immutable<T>(x));
        }

        public static bool operator !=(Immutable<T> x, Immutable<T> y)
        {
            return !(x == y);
        }
        public static bool operator !=(Immutable<T> x, T y)
        {
            return !(x == y);
        }
        public static bool operator !=(T x, Immutable<T> y)
        {
            return !(x == y);
        }
    }

    // Implementation from http://grabbagoft.blogspot.com/2007/06/generic-value-object-equality.html
    public class ValueObject<T> : IEquatable<T>
        where T : ValueObject<T>
    {
        private int? _cachedHash;

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            var other = obj as T;

            return Equals(other);
        }

        public override int GetHashCode()
        {
            // ReSharper disable once NonReadonlyMemberInGetHashCode
            if (_cachedHash.HasValue) return _cachedHash.Value;

            unchecked
            {
                var fields = GetFields();

                const int startValue = 17;
                const int multiplier = 59;

                var hashCode = fields.Select(field => field.GetValue(this)).Where(value => value != null).Aggregate(startValue, (current, value) => (current*multiplier) + value.GetHashCode());

                _cachedHash = hashCode;
            }

            // ReSharper disable once NonReadonlyMemberInGetHashCode
            return _cachedHash.Value;
        }

        public virtual bool Equals(T other)
        {
            if (other == null)
                return false;

            var t = GetType();
            var otherType = other.GetType();

            if (t != otherType)
                return false;

            var fields = t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            foreach (var field in fields)
            {
                var value1 = field.GetValue(other);
                var value2 = field.GetValue(this);

                if (value1 == null)
                {
                    if (value2 != null)
                        return false;
                }
                else if (!value1.Equals(value2))
                    return false;
            }

            return true;
        }

        private IEnumerable<FieldInfo> GetFields()
        {
            var t = GetType();

            var fields = new List<FieldInfo>();

            while (t != typeof(object))
            {
                fields.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));

                t = t.BaseType;
            }

            return fields;
        }

        public static bool operator ==(ValueObject<T> x, ValueObject<T> y)
        {
            return x.Equals(y);
        }

        public static bool operator !=(ValueObject<T> x, ValueObject<T> y)
        {
            return !(x == y);
        }
    }
}