using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    public class DuplicateCommitException : Exception
    {
        public DuplicateCommitException() { }
        public DuplicateCommitException(String message) : base(message) { }
        public DuplicateCommitException(String message, Exception innerException) : base(message, innerException) { }
    }
}
