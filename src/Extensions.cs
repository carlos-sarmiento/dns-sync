using System.Collections.Generic;
using System.Linq;

namespace dns_sync
{
    public static class Extensions
    {
        public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> o) where T : class
        {
            return o.Where(x => x != null)!;
        }
    }
}