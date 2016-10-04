using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    /// <summary>
    /// Used by conflict resolvers to indicate that the resolution has failed and the command needs to be retried
    /// </summary>
    public class AbandonConflictException :Exception
    {
        public AbandonConflictException() { }
        public AbandonConflictException(String Message) : base(Message) { }
    }
}
