using FakeItEasy;
using FakeItEasy.Configuration;
using FluentAssertions.Primitives;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    public static class FakeItEasyFluentAssertionsExtensions
    {
        public static FakeItEasyCallAssertions Should(this IAssertConfiguration configuration)
        {
            return new FakeItEasyCallAssertions(configuration);
        }
    }

    public class FakeItEasyCallAssertions : ReferenceTypeAssertions<IAssertConfiguration, FakeItEasyCallAssertions>
    {
        private IAssertConfiguration _configuration;

        public FakeItEasyCallAssertions(IAssertConfiguration configuration)
        {
            _configuration = configuration;
        }

        protected override string Identifier => "call";
        
        public void HaveHappened(Repeated repeatConstraint = null)
        {
            _configuration.MustHaveHappened(repeatConstraint ?? Repeated.AtLeast.Once);
        }

        public void NotHaveHappened()
        {
            _configuration.MustNotHaveHappened();
        }
    }
    
}
