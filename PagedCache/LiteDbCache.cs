using LiteDB;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace PagedCache
{
    public class PagedCacheResult<T>
    {
        public PagedCacheResult(string next, IEnumerable<T> data)
        {
            Next = next;
            Data = data;
        }

        public string Next { get; private set; }

        public IEnumerable<T> Data { get; private set; }
    }
    public static class PageCache
    {
        private static ConcurrentDictionary<Guid, Thread> _threads = new ConcurrentDictionary<Guid, Thread>();
        private static ConcurrentDictionary<Guid, CacheThread> _cacheThreads = new ConcurrentDictionary<Guid, CacheThread>();

        private static void RemoveThread(Guid threadId)
        {
            Thread.Sleep(30000);
            Thread thread;
            CacheThread cacheThread;
            _threads.TryRemove(threadId, out thread);
            _cacheThreads.TryRemove(threadId, out cacheThread);
        }

        public static PagedCacheResult<T> ExecuteCache<T>(this IDbConnection connection, int pageSize, string sqlCommandString, IDbDataParameter[] parameters = null, int commandTimeout = 300) where T : new()
        {
            var threadId = Guid.NewGuid();
            _cacheThreads.TryAdd(threadId, new CacheThread(threadId, connection, pageSize, sqlCommandString, parameters, commandTimeout));
            _threads.TryAdd(threadId, new Thread(_cacheThreads[threadId].Execute<T>));

            _threads[threadId].Start();
            _cacheThreads[threadId].WaitFristPaged();

            return Next<T>(NextToken(threadId, 1));
        }

        public static PagedCacheResult<T> Next<T>(string token) where T : new()
        {
            return CacheThread.NextPage<T>(GetTokenInfo(token));
        }

        public static void SaveCacheFile(string path)
        {
            CacheThread.SaveStream(path);
        }

        private static string Secret = Guid.NewGuid().ToString("N");

        private static string NextToken(Guid threadId, int page)
        {
            return Jose.JWT.Encode(new Token(threadId, page), Encoding.UTF8.GetBytes(Secret), Jose.JwsAlgorithm.HS256);
        }

        private static Token GetTokenInfo(string token)
        {
            try
            {
                return Jose.JWT.Decode<Token>(token, Encoding.UTF8.GetBytes(Secret), Jose.JwsAlgorithm.HS256);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public class Token
        {
            public Token(Guid threadId, int page)
            {
                Id = threadId;
                Page = page;
            }

            public Guid Id { get; set; }
            public int Page { get; set; }
        }

        public class CacheInfo
        {
            public CacheInfo()
            {
                Pages = new List<PageInfo>();
            }

            public Guid Id { get; set; }
            public int ProcessPage { get; set; }
            public bool IsPagedComplate { get; set; }
            public DateTime ExpiredTime { get; set; }
            public List<PageInfo> Pages { get; set; }
            public class PageInfo
            {
                public int PageNumber { get; set; }
                public string Token { get; set; }
            }
        }

        private class CacheThread
        {

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
                            nextTime = DateTime.Now.AddMinutes(CacheTimeout);
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


            public void WaitFristPaged()
            {
                while (!isReady)
                {
                    ///Waiting for Paged
                }
            }

            private bool isReady { get; set; }

            private static int CacheTimeout = 10;
            private static MemoryStream stream = new MemoryStream();
            private static LiteDatabase db = new LiteDatabase(stream);

            public static void SaveStream(string path)
            {
                var fileStream = new FileStream(path + DateTime.Now.ToString("yyyyMMddHHmmssfff.db"), System.IO.FileMode.CreateNew);
                stream.WriteTo(fileStream);
            }

            Guid _threadId;
            IDbConnection _connection;
            int _pageSize;
            string _sqlCommandString;
            IDbDataParameter[] _parameters;
            int _commandTimeout;

            CacheInfo _cacheInfo;

            public CacheThread(Guid threadId, IDbConnection connection, int pageSize, string sqlCommandString, IDbDataParameter[] parameters, int commandTimeout)
            {
                _cacheInfo = new CacheInfo
                {
                    ExpiredTime = DateTime.Now.AddMinutes(CacheTimeout),
                    IsPagedComplate = false,
                    ProcessPage = 0,
                    Id = _threadId,
                };
                _connection = connection;
                _pageSize = pageSize;
                _sqlCommandString = sqlCommandString;
                _parameters = parameters;
                _commandTimeout = commandTimeout;
            }

            public void Execute<T>() where T : new()
            {
                var pageInfos = db.GetCollection<T>(PageInfoTable);

                if (_connection.State != ConnectionState.Open)
                {
                    _connection.Open();
                }

                using (var command = _connection.CreateCommand())
                {
                    command.CommandText = _sqlCommandString;
                    command.CommandType = CommandType.Text;
                    command.CommandTimeout = _commandTimeout;

                    if (_parameters != null && _parameters.Length > 0)
                    {
                        foreach (var parameter in _parameters)
                        {
                            command.Parameters.Add(parameter);
                        }
                    }

                    using (var reader = command.ExecuteReader())
                    {
                        SaveToCache<T>(reader);
                    }

                    isReady = true;
                    _cacheInfo.IsPagedComplate = true;
                    UpdateCacheInfo();
                }

                RemoveThread(_cacheInfo.Id);

                ClearCache.Run();
            }

            private void SaveToCache<T>(IDataReader reader) where T : new()
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

                    if (list.Count >= _pageSize)
                    {
                        _cacheInfo.ProcessPage++;
                        var collection = db.GetCollection<T>(PageTableName(_threadId, _cacheInfo.ProcessPage));
                        collection.Insert(list);
                        _cacheInfo.Pages.Add(new CacheInfo.PageInfo()
                        {
                            PageNumber = _cacheInfo.ProcessPage,
                            Token = NextToken(_cacheInfo.Id,
                            _cacheInfo.ProcessPage)
                        });
                        list = new List<T>();

                        UpdateCacheInfo();

                        if (_cacheInfo.ProcessPage > 1)
                        {
                            isReady = true;
                        }
                    }
                }

            }

            private void UpdateCacheInfo()
            {
                var collection = db.GetCollection<CacheInfo>(PageInfoTable);

                collection.Upsert(_cacheInfo);
            }

            private static string PageInfoTable = "_PagedInfo";

            private static string PageTableName(Guid threadId, int page)
            {
                return string.Format("{0}_{1}", threadId, page);
            }

            private static CacheInfo GetCacheInfo(Guid threadId)
            {
                var paged = db.GetCollection<CacheInfo>(PageInfoTable);

                return paged.Find(x => x.Id == threadId).FirstOrDefault();
            }

            public static PagedCacheResult<T> NextPage<T>(Token tokenInfo) where T : new()
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

                cacheInfo.ExpiredTime = DateTime.Now.AddMinutes(CacheTimeout);

                var paged = db.GetCollection<T>(PageTableName(tokenInfo.Id, tokenInfo.Page));

                var resultData = paged.FindAll();

                var resultNext = cacheInfo.Pages.FirstOrDefault(x => x.PageNumber == tokenInfo.Page + 1)?.Token ?? string.Empty;

                return new PagedCacheResult<T>(resultNext, resultData);
            }
        }
    }
}
