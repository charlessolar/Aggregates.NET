using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    static class Timer
    {
        public static Task Repeat(Func<Task> action, TimeSpan interval)
        {
            var cts = new CancellationTokenSource();
            return Repeat(action, interval, cts.Token);
        }
        public static async Task Repeat(Func<Task> action, TimeSpan interval, CancellationToken cancellationToken)
        {
            while (true)
            {
                await action().ConfigureAwait(false);
                try
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }

        public static Task Repeat(Func<object, Task> action, object state, TimeSpan interval)
        {
            return Repeat(action, state, interval, CancellationToken.None);
        }
        public static async Task Repeat(Func<object, Task> action, object state, TimeSpan interval, CancellationToken cancellationToken)
        {
            while (true)
            {
                await action(state).ConfigureAwait(false);
                try
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }
        public static Task Expire(Func<object, Task> action, object state, TimeSpan when)
        {
            return Expire(action, state, when, CancellationToken.None);
        }
        public static async Task Expire(Func<object, Task> action, object state, TimeSpan when, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(when, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            await action(state).ConfigureAwait(false);

        }
        public static Task Expire(Func<Task> action, TimeSpan when)
        {
            return Expire(action, when, CancellationToken.None);
        }
        public static async Task Expire(Func<Task> action, TimeSpan when, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(when, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            await action().ConfigureAwait(false);

        }
    }
}
