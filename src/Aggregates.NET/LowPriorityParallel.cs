using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public static class LowPriorityParallel
    {
        public static ParallelLoopResult For(int fromInclusive, int toExclusive, Action<int> body)
        {
            return Parallel.For(fromInclusive, toExclusive, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, TaskScheduler = new LowPriorityTaskScheduler() }, body);
        }
        public static ParallelLoopResult For(long fromInclusive, long toExclusive, Action<long> body)
        {
            return Parallel.For(fromInclusive, toExclusive, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, TaskScheduler = new LowPriorityTaskScheduler() }, body);
        }
        public static ParallelLoopResult ForEach<TSource>(IEnumerable<TSource> source, Action<TSource> body)
        {
            return Parallel.ForEach(source, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, TaskScheduler = new LowPriorityTaskScheduler() }, body);
        }
    }
}
