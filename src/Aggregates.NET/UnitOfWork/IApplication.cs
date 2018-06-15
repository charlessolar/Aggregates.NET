using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.UnitOfWork
{
    public interface IApplication
    {
        /// Used to store things you might need persisted should the message be retried
        /// (if you attempt to save 2 objects and 1 fails, you may want to save the successful one here and ignore it when retrying)
        dynamic Bag { get; set; }
    }
}
