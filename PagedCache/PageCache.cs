using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace PagedCache
{
    public static class PageCache
    {
        public static int TimeOut { get; set; } = 60;

        private static DateTime GetExpiredTime()
        {
            return DateTime.Now.AddMinutes(TimeOut);
        }

        public static PagedCacheResult<T> ExecuteCache<T>(this IDbConnection connection, int pageSize, string sqlCommandString, IDbDataParameter[] parameters = null, int commandTimeout = 300) where T : new()
        {
            var cacheThread = new CacheThread();

            cacheThread.Execute<T>(connection, pageSize, sqlCommandString, parameters, commandTimeout);

            cacheThread.WaitFristPaged();

            threads.TryAdd(cacheThread.Id, cacheThread);

            return Next<T>(NextToken(cacheThread.Id, 1));
        }

        public static PagedCacheResult<T> Next<T>(string token) where T : new()
        {
            var cacheToken = GetCacheToken(token);

            return CacheProcess.NextPage<T>(cacheToken);
        }

        private static ConcurrentDictionary<Guid, CacheThread> threads = new ConcurrentDictionary<Guid, CacheThread>();

        private static void RemoveThread(Guid threadId)
        {
            Thread.Sleep(30000);
            CacheThread thread;
            threads.TryRemove(threadId, out thread);
        }

        private class CacheThread
        {
            public Guid Id { get; private set; }

            public CacheThread()
            {
                Id = Guid.NewGuid();
            }

            private CacheProcess process;

            public void Execute<T>(IDbConnection connection, int pageSize, string sqlCommandString, IDbDataParameter[] parameters, int commandTimeout) where T : new()
            {
                process = new CacheProcess(Id);

                process.Execute<T>(connection, pageSize, sqlCommandString, parameters, commandTimeout);
            }

            public bool IsReady { get; private set; }

            public void WaitFristPaged()
            {
                while (!process.IsReady)
                {
                    //Waiting...
                }
            }
        }

        private class CacheProcess
        {
            public CacheProcess(Guid id)
            {
                _cacheInfo = new CacheInfo
                {
                    Id = id,
                    ExpiredTime = GetExpiredTime(),
                };
            }

            public bool IsReady { get; private set; }

            private CacheInfo _cacheInfo;

            public void Execute<T>(IDbConnection connection, int pageSize, string sqlCommandString, IDbDataParameter[] parameters, int commandTimeout) where T : new()
            {
                var pageInfos = db.GetCollection<T>(PageInfoTable);

                if (connection.State != ConnectionState.Open)
                {
                    connection.Open();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sqlCommandString;
                    command.CommandType = CommandType.Text;
                    command.CommandTimeout = commandTimeout;

                    if (parameters != null && parameters.Length > 0)
                    {
                        foreach (var parameter in parameters)
                        {
                            command.Parameters.Add(parameter);
                        }
                    }

                    using (var reader = command.ExecuteReader())
                    {
                        SaveToCache<T>(reader, pageSize);
                    }

                    IsReady = true;
                    _cacheInfo.IsPagedComplate = true;
                    UpdateCacheInfo();
                }

                RemoveThread(_cacheInfo.Id);

                ClearCache.Run();
            }

            private void SaveToCache<T>(IDataReader reader, int pageSize) where T : new()
            {
                var list = new List<T>();

                var properties = typeof(T).GetProperties().ToList();
                UpdateCacheInfo();

                while (reader.Read())
                {
                    var item = new T();

                    foreach (var property in properties)
                    {
                        property.SetValue(item, reader[property.Name], null);
                    }

                    if (list.Count >= pageSize)
                    {
                        _cacheInfo.ProcessPage++;
                        var collection = db.GetCollection<T>(PageTableName(_cacheInfo.Id, _cacheInfo.ProcessPage));
                        collection.Insert(list);
                        _cacheInfo.Pages.Add(new PageInfo()
                        {
                            PageNumber = _cacheInfo.ProcessPage,
                            Token = NextToken(_cacheInfo.Id, _cacheInfo.ProcessPage)
                        });
                        list = new List<T>();

                        UpdateCacheInfo();

                        if (_cacheInfo.ProcessPage > 1)
                        {
                            IsReady = true;
                        }
                    }
                }
            }

            private void UpdateCacheInfo()
            {
                var collection = db.GetCollection<CacheInfo>(PageInfoTable);

                collection.Upsert(_cacheInfo);
            }

            #region static
            
            private static MemoryStream stream = new MemoryStream();
            private static LiteDB.LiteDatabase db = new LiteDB.LiteDatabase(stream);

            public static PagedCacheResult<T> NextPage<T>(CacheToken tokenInfo) where T : new()
            {
                if (tokenInfo == null || !db.CollectionExists(PageTableName(tokenInfo.Id, tokenInfo.Page)))
                {
                    return new PagedCacheResult<T>(string.Empty, new List<T>());
                }
                var cacheInfo = GetCacheInfo(tokenInfo.Id);
                if (cacheInfo != null || cacheInfo.ExpiredTime < DateTime.Now)
                {
                    return new PagedCacheResult<T>(string.Empty, new List<T>());
                }

                cacheInfo.ExpiredTime = DateTime.Now.AddMinutes(TimeOut);

                var paged = db.GetCollection<T>(PageTableName(tokenInfo.Id, tokenInfo.Page));

                var resultData = paged.FindAll();

                var resultNext = cacheInfo.Pages.FirstOrDefault(x => x.PageNumber == tokenInfo.Page + 1)?.Token ?? string.Empty;

                return new PagedCacheResult<T>(resultNext, resultData);
            }

            private static CacheInfo GetCacheInfo(Guid threadId)
            {
                var paged = db.GetCollection<CacheInfo>(PageInfoTable);

                return paged.Find(x => x.Id == threadId).FirstOrDefault();
            }

            private static string PageInfoTable = "_PageInfo";

            private static string PageTableName(Guid id, int page)
            {
                return $"{id}_{page}";
            }

            #endregion

            private class ClearCache
            {
                private static ClearCache ins;
                private static Thread thread;
                private static object lockObj = new object();

                private ClearCache()
                {
                }

                private void Execute()
                {
                    var collection = db.GetCollection<CacheInfo>(PageInfoTable);

                    while (true)
                    {
                        var cacheInfos = collection.Find(x => x.ExpiredTime < DateTime.Now);
                        foreach (var cacheInfo in cacheInfos)
                        {
                            foreach (var page in cacheInfo.Pages)
                            {
                                var tableName = PageTableName(cacheInfo.Id, page.PageNumber);
                                if (db.CollectionExists(tableName))
                                {
                                    db.DropCollection(tableName);
                                }
                            }
                        }

                        var nextTime = cacheInfos.Min(x => x.ExpiredTime);
                        if (nextTime == DateTime.MinValue)
                        {
                            nextTime = GetExpiredTime();
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

        #region token

        private static string Secret = Guid.NewGuid().ToString("N");

        private static string NextToken(Guid id, int page)
        {
            return Jose.JWT.Encode(new CacheToken(id, page), Encoding.UTF8.GetBytes(Secret), Jose.JwsAlgorithm.HS256);
        }

        private static CacheToken GetCacheToken(string token)
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

        #endregion

        #region model

        private class CacheToken
        {
            public CacheToken()
            {
            }

            public CacheToken(Guid id, int page)
            {
                Id = id;
                Page = page;
            }

            public Guid Id { get; set; }
            public int Page { get; set; }
        }

        private class CacheInfo
        {
            public Guid Id { get; set; }

            public DateTime ExpiredTime { get; set; }
            public bool IsPagedComplate { get; internal set; }
            public int ProcessPage { get; internal set; }

            public List<PageInfo> Pages { get; internal set; }
        }

        private class PageInfo
        {
            public int PageNumber { get; set; }
            public string Token { get; set; }
        }

        #endregion
    }
}