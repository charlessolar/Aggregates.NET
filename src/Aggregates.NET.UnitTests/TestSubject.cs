using System;
using System.Threading.Tasks;
using AutoFixture;

namespace Aggregates
{
    public abstract class TestSubject<T> : Test, IDisposable
    {
        private Lazy<T> _lazy;
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
