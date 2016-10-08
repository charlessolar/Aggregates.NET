using System;

namespace Aggregates.Specifications
{
    // Meant to be a helper class to allow users to define specs like
    // Spec<Entity>.Build<Spec>(parameter).And<Spec2>().Or<Spec3>().Done;
    // But it lacks the ability to group terms at the moment
    public class Spec<T>
    {
        public static Spec<T> Build<TSpec>(params object[] args) where TSpec : Specification<T>, new()
        {
            var spec = (TSpec)Activator.CreateInstance(typeof(TSpec), args);
            return new Spec<T> { Done = spec };
        }

        public Specification<T> Done { get; private set; }

        public Spec<T> And<TSpec>(params object[] args) where TSpec : Specification<T>, new()
        {
            var newSpec = (TSpec)Activator.CreateInstance(typeof(TSpec), args);
            Done = new AndSpecification<T>(Done, newSpec);
            return this;
        }
        public Spec<T> Or<TSpec>(params object[] args) where TSpec : Specification<T>, new()
        {
            var newSpec = (TSpec)Activator.CreateInstance(typeof(TSpec), args);
            Done = new OrSpecification<T>(Done, newSpec);
            return this;
        }
        public Spec<T> Not()
        {
            Done = new NegateSpecification<T>(Done);
            return this;
        }
    }
}
