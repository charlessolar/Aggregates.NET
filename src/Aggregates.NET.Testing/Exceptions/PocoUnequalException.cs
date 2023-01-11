using Newtonsoft.Json;
using System;

namespace Aggregates.Exceptions
{
    public class PocoUnequalException : Exception
    {
        public PocoUnequalException(object pocoPlanned, object pocoChecked) : base($"Poco values unequal\nExpected: {JsonConvert.SerializeObject(pocoPlanned)}\nChecked: {JsonConvert.SerializeObject(pocoChecked)}") { }
    }
}
