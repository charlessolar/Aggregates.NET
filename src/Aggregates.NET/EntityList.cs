using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class EntityList<TEntity, TId> : List<TEntity>
        where TEntity : Entity<TId>
    {
        private readonly IRegisterEntities _aggregateRoot;

        public EntityList(IRegisterEntities aggregateRoot)
        {
            _aggregateRoot = aggregateRoot;
        }

        public EntityList(IRegisterEntities aggregateRoot, int capacity)
            : base(capacity)
        {
            _aggregateRoot = aggregateRoot;
        }

        public EntityList(IRegisterEntities aggregateRoot, IEnumerable<TEntity> collection)
            : base(collection)
        {
            _aggregateRoot = aggregateRoot;
        }

        public new void Add(TEntity entity)
        {
            _aggregateRoot.RegisterChild(entity);
            base.Add(entity);
        }
    }
}
