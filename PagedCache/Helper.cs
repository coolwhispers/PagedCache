using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PagedCache
{
    internal static class Helper
    {
        public static IEnumerable<IEnumerable<T>> ToPaged<T>(this IEnumerable<T> list, int pageSize)
        {
            if (list != null)
            {
                var tempList = list.ToList();
                var remainder = tempList.Count() % pageSize;
                var totalPages = Convert.ToInt32(Math.Ceiling((decimal)tempList.Count() / pageSize));

                for (int i = 1; i < totalPages; i++)
                {
                    yield return tempList.GetRange(pageSize * (i - 1), pageSize);
                }

                if (remainder > 0)
                {
                    yield return tempList.GetRange(tempList.Count - remainder, remainder);
                }
            }
        }

        private static string Secret = Guid.NewGuid().ToString("N");

        public static string EncodeToken(Guid id, int page)
        {
            return Jose.JWT.Encode(new CacheToken(id, page), Encoding.UTF8.GetBytes(Secret), Jose.JwsAlgorithm.HS256);
        }

        public static CacheToken DecodeToken(string token)
        {
            try
            {
                return Jose.JWT.Decode<CacheToken>(token, Encoding.UTF8.GetBytes(Secret), Jose.JwsAlgorithm.HS256);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
