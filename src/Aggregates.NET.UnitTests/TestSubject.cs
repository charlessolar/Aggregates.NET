using AutoFixture;
using System;

namespace Aggregates
{
    public abstract class TestSubject<T> : Test, IDisposable
    {
        private readonly Lazy<T> _lazy;
        protected T Sut => _lazy.Value;

        public TestSubject()
        {
            _lazy = new Lazy<T>(CreateSut);
        }
        public void Dispose()
        {
            if (Sut is IDisposable)
                (Sut as IDisposable).Dispose();
        }

        protected virtual T CreateSut() => Fixture.Create<T>();
    }
}
