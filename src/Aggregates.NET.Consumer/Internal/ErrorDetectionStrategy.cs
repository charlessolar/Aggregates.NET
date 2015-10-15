using Microsoft.Practices.TransientFaultHandling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    // From http://pastebin.com/6tmQbkj4
    public class ErrorDetectionStrategy
    {
        /// <summary>Returns a strategy which retries on the given exception.</summary>
        /// <typeparam name="T">The type of exception to retry on.</typeparam>
        public static ITransientErrorDetectionStrategy On<T>() where T : Exception { return new ExceptionTypeErrorDetectionStrategy<T>(); }
        /// <summary>Returns a strategy which retries on the given exceptions.</summary>
        /// <typeparam name="T">The type of exception to retry on.</typeparam>
        /// <typeparam name="K">The type of exception to retry on.</typeparam>
        public static ITransientErrorDetectionStrategy On<T, K>() where T : Exception where K : Exception { return new TwoExceptionTypeErrorDetectionStrategy<T, K>(); }
    }

    public class ExceptionTypeErrorDetectionStrategy<T> : ITransientErrorDetectionStrategy where T : Exception
    {
        /// <summary>Determines whether the specified exception represents a transient failure that can be compensated by a retry.</summary>
        /// <param name="ex">The exception object to be verified.</param>
        /// <returns>True if the specified exception is considered as transient, otherwise false.</returns>
        public bool IsTransient(Exception ex) { return ex is T; }
    }

    public class TwoExceptionTypeErrorDetectionStrategy<T, K> : ITransientErrorDetectionStrategy where T : Exception where K : Exception
    {
        /// <summary>Determines whether the specified exception represents a transient failure that can be compensated by a retry.</summary>
        /// <param name="ex">The exception object to be verified.</param>
        /// <returns>True if the specified exception is considered as transient, otherwise false.</returns>
        public bool IsTransient(Exception ex) { return ex is T || ex is K; }
    }
}
