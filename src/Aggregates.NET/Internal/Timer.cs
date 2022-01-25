using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Extensions;
using Microsoft.Extensions.Logging;

namespace Aggregates.Internal
{
    static class Timer
    {

        public static Task Repeat(ILogger logger, Func<Task> action, TimeSpan interval, string description)
        {
            return Repeat(logger, action, interval, CancellationToken.None, description);
        }
        public static Task Repeat(ILogger logger, Func<Task> action, TimeSpan interval, CancellationToken cancellationToken, string description)
        {
            return Repeat(logger, (_) => action(), null, interval, cancellationToken, description);
        }

        public static Task Repeat(ILogger logger, Func<object, Task> action, object state, TimeSpan interval, string description)
        {
            return Repeat(logger, action, state, interval, CancellationToken.None, description);
        }
        public static Task Repeat(ILogger logger, Func<object, Task> action, object state, TimeSpan interval, CancellationToken cancellationToken, string description)
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
                        logger.WarnEvent("RepeatFailure", e, "[{Description:l}]: {ExceptionType} - {ExceptionMessage}", description, e.GetType().Name, e.Message);
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
        public static Task Expire(ILogger logger, Func<object, Task> action, object state, TimeSpan when, string description)
        {
            return Expire(logger, action, state, when, CancellationToken.None, description);
        }
        public static Task Expire(ILogger logger, Func<object, Task> action, object state, TimeSpan when, CancellationToken cancellationToken, string description)
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
                    logger.WarnEvent("OnceFailure", e, "[{Description:l}]: {ExceptionType} - {ExceptionMessage}", description, e.GetType().Name, e.Message);
                }
            }, cancellationToken);
        }
        public static Task Expire(ILogger logger, Func<Task> action, TimeSpan when, string description)
        {
            return Expire(logger, action, when, CancellationToken.None, description);
        }
        public static Task Expire(ILogger logger, Func<Task> action, TimeSpan when, CancellationToken cancellationToken, string description)
        {
            return Expire(logger, (_) => action(), null, when, cancellationToken, description);
        }
    }
}
