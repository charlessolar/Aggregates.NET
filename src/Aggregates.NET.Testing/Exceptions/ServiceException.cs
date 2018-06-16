using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Exceptions
{
    public class ServiceException<TService> : Exception
    {
        public ServiceException(string service) : base($"Service {typeof(TService).FullName} body {service} was not requested") { }
    }
}
