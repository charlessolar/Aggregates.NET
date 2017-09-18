using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Contracts
{
    public enum Unit
    {
        Event,
        Message,
        Command,
        Items,
        Errors
    }

    public interface IMetrics
    {
        void Mark(string name, Unit unit, long? value = null);

        void Increment(string name, Unit unit);
        void Decrement(string name, Unit unit);

        void Update(string name, Unit unit, long value);

        ITimer Begin(string name);
    }

    public interface ITimer : IDisposable {
        TimeSpan Elapsed { get; }
    }
}
