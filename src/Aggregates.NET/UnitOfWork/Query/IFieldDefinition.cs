using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.UnitOfWork.Query
{
    public interface IFieldDefinition
    {
        string Field { get; set; }

        string Value { get; set; }

        string Op { get; set; }

        double? Boost { get; set; }
    }
}
