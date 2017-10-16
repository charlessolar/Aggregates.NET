using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Extensions
{
    static class ExceptionExtensions
    {
        public static string AsString(this Exception exception)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Exception type {exception.GetType()}");
            sb.AppendLine($"Exception message: {exception.Message}");
            sb.AppendLine($"Stack trace: {exception.StackTrace}");


            if (exception.InnerException != null)
            {
                sb.AppendLine("---BEGIN Inner Exception--- ");
                sb.AppendLine($"Exception type {exception.InnerException.GetType()}");
                sb.AppendLine($"Exception message: {exception.InnerException.Message}");
                sb.AppendLine($"Stack trace: {exception.InnerException.StackTrace}");
                sb.AppendLine("---END Inner Exception---");

            }
            var aggregateException = exception as System.AggregateException;
            if (aggregateException == null)
                return sb.ToString();

            sb.AppendLine("---BEGIN Aggregate Exception---");
            var aggException = aggregateException;
            foreach (var inner in aggException.InnerExceptions)
            {

                sb.AppendLine("---BEGIN Inner Exception--- ");
                sb.AppendLine($"Exception type {inner.GetType()}");
                sb.AppendLine($"Exception message: {inner.Message}");
                sb.AppendLine($"Stack trace: {inner.StackTrace}");
                sb.AppendLine("---END Inner Exception---");
            }

            return sb.ToString();
        }
    }
}
