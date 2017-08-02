using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PagedCache
{
    public class PagedCacheConfig
    {
        public static int TimeOut { get; set; } = 5;

        internal static DateTime GetExpiredTime()
        {
            return DateTime.Now.AddMinutes(TimeOut);
        }
    }
}
