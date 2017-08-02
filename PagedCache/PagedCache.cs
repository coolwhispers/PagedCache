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
    public static class PagedCache
    {
        public static PagedCacheResult<T> ExecuteCache<T>(this IDbConnection connection, int pageSize, string sqlCommandString, IDbDataParameter[] parameters = null, int commandTimeout = 300) where T : new()
        {
            var cacheThread = new CacheThread();

            cacheThread.Execute<T>(connection, pageSize, sqlCommandString, parameters, commandTimeout);

            cacheThread.WaitFristPaged();

            threads.TryAdd(cacheThread.Id, cacheThread);

            return Next<T>(Helper.EncodeToken(cacheThread.Id, 1));
        }

        public static PagedCacheResult<T> ExecuteCache<T>(this IEnumerable<T> models, int pageSize) where T : new()
        {
            foreach (var item in models.ToPaged(pageSize))
            {

            }

            return null;
        }

        public static PagedCacheResult<T> Next<T>(string token) where T : new()
        {
            var cacheToken = Helper.DecodeToken(token);

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
                    ExpiredTime = PagedCacheConfig.GetExpiredTime(),
                };
            }

            public bool IsReady { get; private set; }

            private CacheInfo _cacheInfo;

            public void Execute<T>(IDbConnection connection, int pageSize, string sqlCommandString, IDbDataParameter[] parameters, int commandTimeout) where T : new()
            {
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
                    DbContext.AddOrUpdateCacheInfo(_cacheInfo);
                }

                RemoveThread(_cacheInfo.Id);
            }

            private void SaveToCache<T>(IDataReader reader, int pageSize) where T : new()
            {
                var list = new List<T>();

                var properties = typeof(T).GetProperties().ToList();
                DbContext.AddOrUpdateCacheInfo(_cacheInfo);

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

                        DbContext.Save<T>(_cacheInfo.Id, _cacheInfo.ProcessPage, list);

                        _cacheInfo.Pages.Add(new PageInfo()
                        {
                            PageNumber = _cacheInfo.ProcessPage,
                            Token = Helper.EncodeToken(_cacheInfo.Id, _cacheInfo.ProcessPage)
                        });

                        DbContext.AddOrUpdateCacheInfo(_cacheInfo);

                        if (_cacheInfo.ProcessPage > 1)
                        {
                            IsReady = true;
                        }

                        list = new List<T>();
                    }
                }
            }

            #region static

            private static MemoryStream stream = new MemoryStream();
            private static LiteDB.LiteDatabase db = new LiteDB.LiteDatabase(stream);

            public static PagedCacheResult<T> NextPage<T>(CacheToken tokenInfo) where T : new()
            {
                var cacheInfo = DbContext.GetCacheInfo(tokenInfo.Id);

                if (cacheInfo != null || cacheInfo.ExpiredTime < DateTime.Now)
                {
                    return new PagedCacheResult<T>(string.Empty, new List<T>());
                }

                cacheInfo.ExpiredTime = PagedCacheConfig.GetExpiredTime();

                var resultData = DbContext.Get<T>(tokenInfo.Id, tokenInfo.Page);

                var resultNext = cacheInfo.Pages.FirstOrDefault(x => x.PageNumber == tokenInfo.Page + 1)?.Token ?? string.Empty;

                return new PagedCacheResult<T>(resultNext, resultData);
            }

            #endregion

        }

    }

    #region model

    internal class CacheToken
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

    internal class PageInfo
    {
        public int PageNumber { get; set; }
        public string Token { get; set; }
    }

    internal class CacheInfo
    {
        public Guid Id { get; set; }

        public DateTime ExpiredTime { get; set; }
        public bool IsPagedComplate { get; internal set; }
        public int ProcessPage { get; internal set; }

        public List<PageInfo> Pages { get; internal set; }
    }

    #endregion
}