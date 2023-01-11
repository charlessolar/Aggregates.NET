namespace Aggregates.Contracts
{
    interface INeedDomainUow
    {
        UnitOfWork.IDomainUnitOfWork Uow { get; set; }
    }
}
