using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    public class StorageException : Exception
    {
        public StorageException()
        {
        }

        public StorageException(String message)
            : base(message)
        {
        }

        public StorageException(String message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}