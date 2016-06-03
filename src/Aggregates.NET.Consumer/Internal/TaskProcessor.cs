using NServiceBus.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class TaskProcessor : IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(TaskProcessor));

        private static CancellationTokenSource _cancelToken = new CancellationTokenSource();
        private static ManualResetEvent _pauseEvent = new ManualResetEvent(true);
        private static LinkedList<Func<Task>> _tasks = new LinkedList<Func<Task>>(); // protected by lock(_tasks)
        private readonly List<Thread> _threads = new List<Thread>();
        // The list of tasks to be executed 

        // The maximum concurrency level allowed by this scheduler. 
        private readonly int _maxDegreeOfParallelism;

        // Creates a new instance with the specified degree of parallelism. 
        public TaskProcessor(int maxDegreeOfParallelism)
        {
            if (maxDegreeOfParallelism < 1) throw new ArgumentOutOfRangeException("maxDegreeOfParallelism");
            _maxDegreeOfParallelism = maxDegreeOfParallelism;

            for (var i = 0; i < maxDegreeOfParallelism; i++)
            {
                var thread = new Thread(DoWork);
                thread.IsBackground = true;
                
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

        public void Pause(Boolean Pause)
        {
            if (Pause)
            {
                Logger.Warn("Pausing event processing");
                _pauseEvent.Reset();
            }
            else
            {
                Logger.Warn("Resuming event processing");
                _pauseEvent.Set();
            }
        }
        
        private static void DoWork()
        {
            while (!_cancelToken.IsCancellationRequested)
            {
                _pauseEvent.WaitOne(Timeout.Infinite);

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
                try
                {
                    // Process the event
                    item().Wait();
                }
                catch (AggregateException) { }
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
