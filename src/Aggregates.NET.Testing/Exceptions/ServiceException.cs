using System;

namespace Aggregates.Exceptions
{
    public class ServiceException : Exception
    {
        public ServiceException(Type service, string payload) : base($"Service {service.FullName} body {payload} was not requested") { }
    }
}
