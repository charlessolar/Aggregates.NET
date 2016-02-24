using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Extensions
{
    public static class ThreadExtensions
    {
        public static void Rename(this System.Threading.Thread thread, String name)
        {
            lock (thread)
            {
                thread.GetType().
                    GetField("m_Name", BindingFlags.Instance | BindingFlags.NonPublic).
                    SetValue(thread, null);
                thread.Name = name;
            }
        }
    }
}
