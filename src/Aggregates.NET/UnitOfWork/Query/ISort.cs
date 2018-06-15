using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.UnitOfWork.Query
{
    public interface ISort
    {
        string Field { get; set; }
        string Dir { get; set; }
    }
}
