using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Attributes
{
    /// <summary>
    /// Marks a class or method as able to be executed in parallel with other tasks
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Interface)]
    public class ParallelAttribute : Attribute
    {
    }
}
