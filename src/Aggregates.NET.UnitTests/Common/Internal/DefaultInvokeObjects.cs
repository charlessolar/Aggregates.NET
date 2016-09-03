using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.NET.UnitTests.Common.Internal
{
    interface ITest { }

    class Handler :
        IHandleMessagesAsync<ITest>
    {
        public Task Handle(ITest e, IHandleContext ctx) { return Task.FromResult(0); }
    }

    [TestFixture]
    public class DefaultInvokeObjects
    {
        [SetUp]
        public void Setup() { }

        [Test]
        public void resolve_invoke()
        {
            var invoker = new Aggregates.Internal.DefaultInvokeObjects();

            var invoke = invoker.Invoker(new Handler(), typeof(ITest));

            Assert.NotNull(invoke);
        }
        [Test]
        public void resolve_unknown()
        {
            var invoker = new Aggregates.Internal.DefaultInvokeObjects();
            var invoke = invoker.Invoker(new Handler(), typeof(int));

            Assert.Null(invoke);
        }
        [Test]
        public void resolve_invoke_cached()
        {
            var invoker = new Aggregates.Internal.DefaultInvokeObjects();

            var invoke = invoker.Invoker(new Handler(), typeof(ITest));
            var invoke2 = invoker.Invoker(new Handler(), typeof(ITest));

            Assert.NotNull(invoke2);
        }
    }
}
