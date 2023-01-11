namespace Aggregates.UnitOfWork.Query
{
    public interface IGrouped
    {
        string Group { get; set; }
        IFieldDefinition[] Definitions { get; set; }
    }
}
