using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class PersistenceException : Exception
    {
        public PersistenceException() { }
        public PersistenceException(String message) : base(message) { }
        public PersistenceException(String message, Exception innerException) : base(message, innerException) { }
    }
}
