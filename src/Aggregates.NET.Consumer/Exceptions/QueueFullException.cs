using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    public class QueueFullException : Exception
    {
        public QueueFullException(String Message) : base(Message) { }
    }
}
