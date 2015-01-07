using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Commands
{
    public interface Reject
    {
        String Message { get; set; }
    }
}
