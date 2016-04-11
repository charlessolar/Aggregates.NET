using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class EventProcessor : IDisposable
    {

        private static CancellationTokenSource _cancelToken = new CancellationTokenSource();
        private static LinkedList<Func<Task>> _tasks = new LinkedList<Func<Task>>(); // protected by lock(_tasks)
        private readonly List<Thread> _threads = new List<Thread>();
        // The list of tasks to be executed 

        // The maximum concurrency level allowed by this scheduler. 
        private readonly int _maxDegreeOfParallelism;

        // Creates a new instance with the specified degree of parallelism. 
        public EventProcessor(int maxDegreeOfParallelism)
        {
            if (maxDegreeOfParallelism < 1) throw new ArgumentOutOfRangeException("maxDegreeOfParallelism");
            _maxDegreeOfParallelism = maxDegreeOfParallelism;

            for (var i = 0; i < maxDegreeOfParallelism; i++)
            {
                var thread = new Thread(DoWork);
                _threads.Add(thread);
                thread.Start();
            }
        }

        // Queues a task to the scheduler. 
        public void Queue(Func<Task> task)
        {
            // Add the task to the list of tasks to be processed.  If there aren't enough 
            // delegates currently queued or running to process tasks, schedule another. 
            lock (_tasks)
                _tasks.AddLast(task);
        }

        // Inform the ThreadPool that there's work to be executed for this scheduler. 
        private static void DoWork()
        {
            while (!_cancelToken.IsCancellationRequested)
            {
                Func<Task> item;
                lock (_tasks)
                {
                    if (_tasks.Count == 0)
                    {
                        item = null;
                    }
                    else
                    {
                        // Get the next item from the queue
                        item = _tasks.First.Value;
                        _tasks.RemoveFirst();
                    }
                }
                if(item == null)
                {
                    Thread.Sleep(250);
                    continue;
                }
                // Process the event
                item().Wait();
            }            
        }

        public void Dispose()
        {
            _cancelToken.Cancel();
        }

        // Gets the maximum concurrency level supported by this scheduler. 
        public int MaximumConcurrencyLevel { get { return _maxDegreeOfParallelism; } }
        
    }
}
