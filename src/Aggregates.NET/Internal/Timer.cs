using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Extensions;
using NServiceBus.Logging;

namespace Aggregates.Internal
{
    static class Timer
    {
        private static readonly ILog Logger = LogManager.GetLogger("Timer");

        public static Task Repeat(Func<Task> action, TimeSpan interval, string description)
        {
            var cts = new CancellationTokenSource();
            return Repeat(action, interval, cts.Token, description);
        }
        public static Task Repeat(Func<Task> action, TimeSpan interval, CancellationToken cancellationToken, string description)
        {
            return Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        await action().ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Logger.Warn($"Repeating timer [{description}] threw an exception: {e.GetType().Name} {e.Message}", e);
                    }
                    try
                    {
                        await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }
                }
            }, cancellationToken);
        }

        public static Task Repeat(Func<object, Task> action, object state, TimeSpan interval, string description)
        {
            return Repeat(action, state, interval, CancellationToken.None, description);
        }
        public static Task Repeat(Func<object, Task> action, object state, TimeSpan interval, CancellationToken cancellationToken, string description)
        {
            return Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        await action(state).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Logger.Warn($"Repeating timer [{description}] threw an exception: {e.GetType().Name} {e.Message}", e);
                    }
                    try
                    {
                        await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }
                }
            }, cancellationToken);
        }
        public static Task Expire(Func<object, Task> action, object state, TimeSpan when, string description)
        {
            return Expire(action, state, when, CancellationToken.None, description);
        }
        public static Task Expire(Func<object, Task> action, object state, TimeSpan when, CancellationToken cancellationToken, string description)
        {
            return Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(when, cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
                try
                {
                    await action(state).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.Warn($"One-time timer [{description}] threw an exception: {e.GetType().Name} {e.Message}", e);
                }
            }, cancellationToken);
        }
        public static Task Expire(Func<Task> action, TimeSpan when, string description)
        {
            return Expire(action, when, CancellationToken.None, description);
        }
        public static Task Expire(Func<Task> action, TimeSpan when, CancellationToken cancellationToken, string description)
        {
            return Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(when, cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
                try
                {
                    await action().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.Warn($"One-time timer [{description}] threw an exception: {e.GetType().Name} {e.Message}", e);
                }
            }, cancellationToken);
        }
    }
}
