using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Internal
{
    public class ParentDescriptor : IParentDescriptor
    {
        public string EntityType { get; set; }
        public Id Id { get; set; }
    }
}
