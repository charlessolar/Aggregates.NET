using AutoFixture;
using AutoFixture.AutoFakeItEasy;
using AutoFixture.Xunit2;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    public class AutoFakeItEasyDataAttribute : AutoDataAttribute
    {
        public AutoFakeItEasyDataAttribute()
            : base(() => new Fixture().Customize(new AutoFakeItEasyCustomization()))
        {
        }
    }
}
