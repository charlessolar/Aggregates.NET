using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Extensions
{
    public static class StringExtensions
    {
        public static String MaxLength(this String source, Int32 Max)
        {
            if (source.Length < Max)
                return source;
            return source.Substring(0, Max);
        }
        public static String MaxLines(this String source, Int32 Max)
        {
            var ret = "";
            var lines = 0;
            while(lines <= Max)
            {
                var newline = source.IndexOf("\n");
                if (newline == -1) break;
                ret += source.Substring(0, newline + 1);
                source = source.Substring(newline + 1);
                lines++;
            }

            return ret;
        }
    }
}
