using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.UnitOfWork.Query
{
    public interface IQueryResult<T> where T : class
    {
        T[] Records { get; set; }
        long Total { get; set; }
        long ElapsedMs { get; set; }
    }
}
