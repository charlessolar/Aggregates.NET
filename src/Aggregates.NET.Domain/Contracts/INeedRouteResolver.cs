namespace Aggregates.Contracts
{
    public interface INeedRouteResolver
    {
        IRouteResolver Resolver { get; set; }
    }
}
