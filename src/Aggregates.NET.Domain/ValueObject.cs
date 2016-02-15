using Aggregates.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class SingleValueObject<T> : ValueObject<SingleValueObject<T>>
    {
        public readonly T Value;

        public SingleValueObject(T Value = default(T))
        {
            this.Value = Value;
        }

        public Boolean HasValue
        {
            get
            {
                return this.Value != null;
            }
        }

        public override String ToString()
        {
            if (!this.HasValue) return "";
            return this.Value.ToString();
        }
    }

    // Implementation from http://grabbagoft.blogspot.com/2007/06/generic-value-object-equality.html
    public abstract class ValueObject<T> : IEquatable<T>
        where T : ValueObject<T>
    {
        private Int32? _cachedHash;

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            T other = obj as T;

            return Equals(other);
        }

        public override int GetHashCode()
        {
            if (this._cachedHash.HasValue) return this._cachedHash.Value;

            unchecked
            {
                IEnumerable<FieldInfo> fields = GetFields();

                int startValue = 17;
                int multiplier = 59;

                int hashCode = startValue;

                foreach (FieldInfo field in fields)
                {
                    object value = field.GetValue(this);

                    if (value != null)
                        hashCode = (hashCode * multiplier) + value.GetHashCode();
                }

                this._cachedHash = hashCode;
            }

            return this._cachedHash.Value;
        }

        public virtual bool Equals(T other)
        {
            if (other == null)
                return false;

            Type t = GetType();
            Type otherType = other.GetType();

            if (t != otherType)
                return false;

            FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            foreach (FieldInfo field in fields)
            {
                object value1 = field.GetValue(other);
                object value2 = field.GetValue(this);

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
            Type t = GetType();

            List<FieldInfo> fields = new List<FieldInfo>();

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