using Aggregates.Contracts;

namespace Aggregates.Internal
{
    public class ParentDescriptor : IParentDescriptor
    {
        public string EntityType { get; set; }
        public Id StreamId { get; set; }
    }
}
