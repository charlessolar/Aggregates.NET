using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Exceptions
{
    public class ModelException<TModel> : Exception
    {
        public ModelException(Id id, string op) : base($"Model {typeof(TModel).Name} {id} was not {op}") { }
        public ModelException(Id id, TModel model) : base($"Model {typeof(TModel).Name} {id} failed check - \n{JsonConvert.SerializeObject(model)}") { }
    }
}
