using System.Runtime.Serialization;

namespace Aggregates.Internal.Cloning
{
    class ObjectUniquenessManager<T> where T : class
    {
        #region Fields

        private readonly ObjectIDGenerator objectIdGenerator;

        #endregion

        #region Ctors

        public ObjectUniquenessManager()
        {
            this.objectIdGenerator = new ObjectIDGenerator();
        }

        #endregion

        /// <summary>
        ///     Determines whether [is first encounter].
        ///     Useful when you want to know if you have come across this object before without keeping references manually
        ///     Observer nUnitUtilies.TestDeepClone for good application of this
        /// </summary>
        /// <returns></returns>
        public bool IsFirstEncounter(T targetObject)
        {
            bool isFirst;
            this.objectIdGenerator.GetId(targetObject, out isFirst);
            return isFirst;
        }
    }
}