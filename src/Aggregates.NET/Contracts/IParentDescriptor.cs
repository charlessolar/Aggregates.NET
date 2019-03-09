using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Contracts
{
    public interface IParentDescriptor
    {
        string EntityType { get; set; }
        Id Id { get; set; }
    }
}
