namespace Aggregates.Contracts
{
    interface INeedVersionRegistrar
    {
        IVersionRegistrar Registrar { get; set; }
    }
}
