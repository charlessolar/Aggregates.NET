using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    public class SubscriptionCanceled : Exception
    {
        public SubscriptionCanceled() : base() { }
        public SubscriptionCanceled(String message) : base(message) { }
    }
}
