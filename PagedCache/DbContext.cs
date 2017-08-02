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
        private static readonly Stream DbStream = new MemoryStream();
        private static readonly LiteDB.LiteDatabase Db = new LiteDB.LiteDatabase(DbStream);

        private static readonly string PageInfoTable = "_PagedCache";

        private static readonly string ExpiredTimeTable = "_ExpiredTime";

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

            var table = Db.GetCollection<T>(tableName);

            table.Insert(models);

            return tableName;
        }

        public static IEnumerable<T> Get<T>(Guid id, int page)
        {
            var table = Db.GetCollection<T>(GetTableName(id, page));

            return table.FindAll();
        }

        public static void AddOrUpdateCacheInfo(CacheInfo model)
        {
            var table = Db.GetCollection<CacheInfo>(PageInfoTable);

            table.Upsert(model);

            ClearCache.Run();
        }

        public static CacheInfo GetCacheInfo(Guid id)
        {
            var table = Db.GetCollection<CacheInfo>(PageInfoTable);

            return table.Find(x => x.Id == id).FirstOrDefault();
        }

        public static DateTime GetExpiredTime(Guid id)
        {
            var table = Db.GetCollection<CacheExpiredTime>(ExpiredTimeTable);

            return table.Find(x => x.Id == id).Select(x => x.ExpiredTime).FirstOrDefault();
        }

        public static void UpdateExpiredTime(CacheExpiredTime model)
        {
            var table = Db.GetCollection<CacheExpiredTime>(ExpiredTimeTable);

            table.Upsert(model);
        }

        public static void DropCache(CacheInfo cacheInfo)
        {
            for (var page = 1; page <= cacheInfo.TotalPageCount; page++)
            {
                var tableName = GetTableName(cacheInfo.Id, page);

                if (Db.CollectionExists(tableName))
                {
                    Db.DropCollection(tableName);
                }
            }

            if (Db.CollectionExists(PageInfoTable))
            {
                Db.DropCollection(PageInfoTable);
            }

            if (Db.CollectionExists(ExpiredTimeTable))
            {
                Db.DropCollection(ExpiredTimeTable);
            }
        }
        

        public static IEnumerable<CacheInfo> GetExpiredCache()
        {
            var table = Db.GetCollection<CacheExpiredTime>(ExpiredTimeTable);

            var expiredIds = table.Find(x => x.ExpiredTime < DateTime.Now).Select(x => x.Id);

            var collection = Db.GetCollection<CacheInfo>(ExpiredTimeTable);

            return collection.Find(x => expiredIds.Contains(x.Id));
        }

        public static DateTime GetMinExpiredCache()
        {
            var collection = Db.GetCollection<CacheExpiredTime>(ExpiredTimeTable);

            return collection.Min(x => x.ExpiredTime);
        }

    }
    internal class ClearCache
    {
        private static Thread _thread;
        private static readonly object LockObj = new object();
        
        private static void Execute()
        {
            while (true)
            {
                var cacheInfos = DbContext.GetExpiredCache();
                foreach (var cacheInfo in cacheInfos)
                {
                    DbContext.DropCache(cacheInfo);
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
            if (_thread == null || _thread.ThreadState != ThreadState.Running)
            {
                lock (LockObj)
                {
                    if (_thread == null || _thread.ThreadState != ThreadState.Running)
                    {
                        _thread = new Thread(Execute);
                        _thread.Start();
                    }
                }
            }
        }
    }
}
