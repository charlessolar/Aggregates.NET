using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.UnitOfWork.Query
{
    public interface IGrouped
    {
        string Group { get; set; }
        IFieldDefinition[] Definitions { get; set; }
    }
}
