using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Exceptions
{
    public class ModelException : Exception
    {
        public ModelException(Type tmodel, Id id, string op) : base($"Model {tmodel.Name} {id} was not {op}") { }
        public ModelException(Type tmodel, Id id, object model) : base($"Model {tmodel.Name} {id} failed check - \n{JsonConvert.SerializeObject(model)}") { }
    }
}
