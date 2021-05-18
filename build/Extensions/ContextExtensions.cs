using Cake.Core;
using Cake.Core.Diagnostics;
using System;
using System.Collections.Generic;
using System.Text;

namespace Build.Extensions
{
    public static class ContextExtensions
    {
        public static void Info(this ICakeContext context, string format, params object[] args)
        {
            context.Log.Information(format, args);
        }
    }
}
