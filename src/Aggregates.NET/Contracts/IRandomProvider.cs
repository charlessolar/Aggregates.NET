using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Contracts
{
    public interface IRandomProvider
    {
        bool Chance(int percent);
    }
}
