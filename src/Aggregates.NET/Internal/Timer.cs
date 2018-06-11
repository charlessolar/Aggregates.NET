using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Logging;
using Aggregates.Extensions;

namespace Aggregates.Internal
{
    static class Timer
    {
        private static readonly ILog Logger = LogProvider.GetLogger("Timer");

        public static Task Repeat(Func<Task> action, TimeSpan interval, string description)
        {
            return Repeat(action, interval, CancellationToken.None, description);
        }
        public static Task Repeat(Func<Task> action, TimeSpan interval, CancellationToken cancellationToken, string description)
        {
            return Repeat((_) => action(), null, interval, cancellationToken, description);
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
                        Logger.WarnEvent("RepeatFailure", e, "[{description:l}]: {ExceptionType} - {ExceptionMessage}", description, e.GetType().Name, e.Message);
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
                    Logger.WarnEvent("OnceFailure", e, "[{description:l}]: {ExceptionType} - {ExceptionMessage}", description, e.GetType().Name, e.Message);
                }
            }, cancellationToken);
        }
        public static Task Expire(Func<Task> action, TimeSpan when, string description)
        {
            return Expire(action, when, CancellationToken.None, description);
        }
        public static Task Expire(Func<Task> action, TimeSpan when, CancellationToken cancellationToken, string description)
        {
            return Expire((_) => action(), null, when, cancellationToken, description);
        }
    }
}
