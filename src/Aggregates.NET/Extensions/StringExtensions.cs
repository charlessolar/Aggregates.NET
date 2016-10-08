using System;

namespace Aggregates.Extensions
{
    public static class StringExtensions
    {
        public static string MaxLength(this string source, int max)
        {
            if (source.Length < max)
                return source;
            return source.Substring(0, max);
        }
        public static string MaxLines(this string source, int max)
        {
            var ret = "";
            var lines = 0;
            while(lines < max)
            {
                var newline = source.IndexOf("\n", StringComparison.Ordinal);
                if (newline == -1) break;
                ret += source.Substring(0, newline + 1);
                source = source.Substring(newline + 1);
                lines++;
            }

            return ret;
        }
    }
}
