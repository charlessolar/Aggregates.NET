using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    public class CommandRejectedException : Exception
    {
        public CommandRejectedException()
        {
        }

        public CommandRejectedException(String message)
            : base(message)
        {
        }

        public CommandRejectedException(String message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
