using System;
using AutoFixture;

namespace Aggregates
{
    public abstract class TestSubject<T> : Test
    {
        private Lazy<T> _lazy;
        protected T Sut => _lazy.Value;

        public TestSubject()
        {
            _lazy = new Lazy<T>(CreateSut);
        }

        protected virtual T CreateSut() => Fixture.Create<T>();
    }
}
