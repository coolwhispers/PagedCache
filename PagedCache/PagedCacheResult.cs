using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PagedCache
{
    public class PagedCacheResult<T> : IEnumerable<T>
    {
        public PagedCacheResult(string next, IEnumerable<T> data)
        {
            Next = next;
            Data = data;
        }

        public string Next { get; private set; }

        public IEnumerable<T> Data { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            return Data.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
