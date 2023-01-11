namespace Aggregates.Contracts
{
    public interface IRandomProvider
    {
        bool Chance(int percent);
    }
}
