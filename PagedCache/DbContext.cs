using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PagedCache
{
    internal class DbContext
    {
        private static Stream stream = new MemoryStream();
        private static LiteDB.LiteDatabase db = new LiteDB.LiteDatabase(stream);

        private static string PageInfoTable = "_PagedCache";

        private static string GetTableName(Guid id, int page)
        {
            return string.Format("{0}_{1}", id, page);
        }

        /// <summary>
        /// Saves the cache.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id">The identifier.</param>
        /// <param name="page">The page.</param>
        /// <param name="models">The models.</param>
        public static string Save<T>(Guid id, int page, IEnumerable<T> models)
        {
            var tableName = GetTableName(id, page);

            var table = db.GetCollection<T>(tableName);

            table.Insert(models);

            return tableName;
        }

        public static IEnumerable<T> Get<T>(Guid id, int page)
        {
            var table = db.GetCollection<T>(GetTableName(id, page));

            return table.FindAll();
        }

        public static void AddOrUpdateCacheInfo(CacheInfo model)
        {
            var table = db.GetCollection<CacheInfo>(PageInfoTable);

            table.Upsert(model);

            ClearCache.Run();
        }

        public static CacheInfo GetCacheInfo(Guid id)
        {
            var table = db.GetCollection<CacheInfo>(PageInfoTable);

            return table.Find(x => x.Id == id).FirstOrDefault();
        }

        public static void DropTable(Guid id, int page)
        {
            var tableName = GetTableName(id, page);

            if (db.CollectionExists(tableName))
            {
                db.DropCollection(tableName);
            }
        }

        public static IEnumerable<CacheInfo> GetExpiredCache()
        {
            var collection = db.GetCollection<CacheInfo>(PageInfoTable);

            return collection.Find(x => x.ExpiredTime < DateTime.Now);
        }

        public static DateTime GetMinExpiredCache()
        {
            var collection = db.GetCollection<CacheInfo>(PageInfoTable);

            return collection.Min(x => x.ExpiredTime);
        }

    }
    internal class ClearCache
    {
        private static ClearCache ins;
        private static Thread thread;
        private static object lockObj = new object();

        private ClearCache()
        {
        }

        private void Execute()
        {
            while (true)
            {
                var cacheInfos = DbContext.GetExpiredCache();
                foreach (var cacheInfo in cacheInfos)
                {
                    foreach (var page in cacheInfo.Pages)
                    {
                        DbContext.DropTable(cacheInfo.Id, page.PageNumber);
                    }
                }

                var nextTime = DbContext.GetMinExpiredCache();

                if (nextTime == DateTime.MinValue)
                {
                    nextTime = PagedCacheConfig.GetExpiredTime();
                }

                Thread.Sleep(nextTime - DateTime.Now);
            }
        }

        public static void Run()
        {
            if (ins == null)
            {
                lock (lockObj)
                {
                    if (ins == null)
                    {
                        ins = new ClearCache();
                    }
                }
            }

            if (thread == null || thread.ThreadState != ThreadState.Running)
            {
                lock (lockObj)
                {
                    if (thread == null || thread.ThreadState != ThreadState.Running)
                    {
                        thread = new Thread(ins.Execute);
                        thread.Start();
                    }
                }
            }
        }
    }
}
