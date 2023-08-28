using Aggregates.UnitOfWork;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Extensions {
    public static class IMessageHandlerContextExtensions {
        public static IApplicationUnitOfWork App(this IMessageHandlerContext context) {
            return context.Application<TestableApplication>();
        }
    }
}
