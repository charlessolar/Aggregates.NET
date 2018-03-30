using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Attributes
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
    public class KeyProperty : Attribute
    {
        public KeyProperty(bool always = false)
        {
            this.Always = always;
        }

        public bool Always { get; private set; }
    }
}
