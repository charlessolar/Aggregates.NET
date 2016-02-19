using System;
using System.ComponentModel;

namespace Aggregates.Contracts
{
    //From Kzu's blog: http://www.clariusconsulting.net/blogs/kzu/archive/2008/03/10/58301.aspx
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IFluentInterface
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        Type GetType();

        [EditorBrowsable(EditorBrowsableState.Never)]
        int GetHashCode();

        [EditorBrowsable(EditorBrowsableState.Never)]
        string ToString();

        [EditorBrowsable(EditorBrowsableState.Never)]
        bool Equals(object obj);
    }
}
