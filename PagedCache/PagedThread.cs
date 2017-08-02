using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PagedCache
{
    internal class PagedThread
    {
        private static readonly ConcurrentDictionary<Guid, CacheThread> Threads = new ConcurrentDictionary<Guid, CacheThread>();

        public static void Remove(Guid threadId)
        {
            Thread.Sleep(30000);
            CacheThread thread;
            Threads.TryRemove(threadId, out thread);
        }

        public static void Add(CacheThread cacheThread)
        {
            Threads.TryAdd(cacheThread.Id, cacheThread);
        }
    }
}
