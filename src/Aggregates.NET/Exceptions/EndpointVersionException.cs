using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    public class EndpointVersionException : Exception
    {
        public EndpointVersionException(string projection, string current, string desired) 
            : base($"Projection [{projection}] already exists and is a different version!  If you've upgraded your code don't forget to bump your app's version!\nExisting:\n{current}\nDesired:\n{desired}")
        {
        }
    }
}
