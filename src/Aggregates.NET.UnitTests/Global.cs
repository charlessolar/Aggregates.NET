using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus.Logging;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests
{
    [SetUpFixture]
    public class Global
    {
        [OneTimeSetUp]
        public void Setup()
        {
            var defaultFactory = LogManager.Use<DefaultFactory>();
            defaultFactory.Level(LogLevel.Fatal);
        }
    }
}
