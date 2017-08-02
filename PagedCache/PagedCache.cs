using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace PagedCache
{
    public static class PagedCache
    {
        /// <summary>
        /// Get cache id
        /// </summary>
        /// <param name="token">The token.</param>
        /// <returns></returns>
        public static Guid GetCacheId(string token)
        {
            var cache = Helper.DecodeToken(token);

            return cache.Id;
        }

        /// <summary>
        /// Execute Cache
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="connection">The connection.</param>
        /// <param name="pageSize">Size of the page.</param>
        /// <param name="sqlCommandString">The SQL command string.</param>
        /// <param name="parameters">The parameters.</param>
        /// <param name="commandTimeout">The command timeout.</param>
        /// <returns></returns>
        public static PagedCacheResult<T> ExecuteCache<T>(this IDbConnection connection, int pageSize, string sqlCommandString, IDbDataParameter[] parameters = null, int commandTimeout = 300) where T : new()
        {
            var cacheThread = new CacheThread();

            cacheThread.Execute<T>(connection, pageSize, sqlCommandString, parameters, commandTimeout);

            cacheThread.WaitFristPaged();

            PagedThread.Add(cacheThread);

            return Next<T>(Helper.EncodeToken(cacheThread.Id, 1));
        }

        /// <summary>
        /// Execute Cache
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="models">The models.</param>
        /// <param name="pageSize">Size of the page.</param>
        /// <returns></returns>
        public static PagedCacheResult<T> ExecuteCache<T>(this IEnumerable<T> models, int pageSize) where T : new()
        {
            var process = new CacheProcess(pageSize);

            process.ExecuteForList(models);

            return Next<T>(Helper.EncodeToken(process.Id, 1));
        }

        /// <summary>
        /// Get next page by token
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="token">The token.</param>
        /// <returns></returns>
        public static PagedCacheResult<T> Next<T>(string token) where T : new()
        {
            var cacheToken = Helper.DecodeToken(token);

            return Next<T>(cacheToken);
        }

        /// <summary>
        /// Get next page by cache id & page
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id">The identifier.</param>
        /// <param name="page">The page.</param>
        /// <returns></returns>
        public static PagedCacheResult<T> Next<T>(Guid id, int page) where T : new()
        {
            return Next<T>(new CacheToken { Id = id, Page = page });
        }

        private static PagedCacheResult<T> Next<T>(CacheToken tokenInfo) where T : new()
        {
            var cacheInfo = DbContext.GetCacheInfo(tokenInfo.Id);

            if (cacheInfo == null || DbContext.GetExpiredTime(tokenInfo.Id) < DateTime.Now)
            {
                return new PagedCacheResult<T>(string.Empty, new List<T>());
            }

            DbContext.UpdateExpiredTime(new CacheExpiredTime { Id = tokenInfo.Id, ExpiredTime = PagedCacheConfig.GetExpiredTime() });

            var resultData = DbContext.Get<T>(tokenInfo.Id, tokenInfo.Page);

            return new PagedCacheResult<T>(Helper.EncodeToken(tokenInfo.Id, tokenInfo.Page + 1), resultData);
        }
    }

    internal class CacheProcess
    {
        public Guid Id => _cacheInfo.Id;

        public bool IsReady { get; set; }

        private readonly CacheInfo _cacheInfo;

        public CacheProcess(int pageSize) : this(Guid.NewGuid(), pageSize)
        {
        }

        public CacheProcess(Guid id, int pageSize)
        {
            _cacheInfo = new CacheInfo
            {
                Id = id,
                PageSize = pageSize,
            };
        }

        private void CalculatePage(int totalCount)
        {
            _cacheInfo.TotalCount = totalCount;

            _cacheInfo.TotalPageCount = _cacheInfo.TotalCount / _cacheInfo.PageSize;

            if (_cacheInfo.TotalCount % _cacheInfo.PageSize != 0)
            {
                _cacheInfo.TotalPageCount++;
            }
        }

        public void ExecuteForList<T>(IEnumerable<T> models) where T : new()
        {
            CalculatePage(models.Count());

            foreach (var paged in models.ToPaged(_cacheInfo.PageSize))
            {
                SaveToCache(paged);
            }

            Complate();
        }

        public void ExecuteForSql<T>(IDbConnection connection, string sqlCommandString, IDbDataParameter[] parameters, int commandTimeout) where T : new()
        {
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT COUNT(*) FROM ( {sqlCommandString} )";
                command.CommandType = CommandType.Text;
                command.CommandTimeout = commandTimeout;

                if (parameters != null && parameters.Length > 0)
                {
                    foreach (var parameter in parameters)
                    {
                        command.Parameters.Add(parameter);
                    }
                }

                CalculatePage(Convert.ToInt32(command.ExecuteScalar()));
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
                    var properties = typeof(T).GetProperties().ToList();

                    var list = new List<T>();
                    while (reader.Read())
                    {
                        var item = new T();

                        foreach (var property in properties)
                        {
                            property.SetValue(item, reader[property.Name], null);
                        }

                        if (list.Count >= _cacheInfo.PageSize)
                        {
                            SaveToCache(list);

                            list = new List<T>();
                        }
                    }
                }
            }

            Complate();
        }

        private void SaveToCache<T>(IEnumerable<T> models) where T : new()
        {
            _cacheInfo.ProcessPage++;

            DbContext.Save(_cacheInfo.Id, _cacheInfo.ProcessPage, models);

            DbContext.AddOrUpdateCacheInfo(_cacheInfo);

            DbContext.UpdateExpiredTime(new CacheExpiredTime
            {
                Id = _cacheInfo.Id,
                ExpiredTime = PagedCacheConfig.GetExpiredTime()
            });

            if (_cacheInfo.ProcessPage > 1)
            {
                IsReady = true;
            }
        }

        private void Complate()
        {
            IsReady = true;

            _cacheInfo.IsComplate = true;

            DbContext.AddOrUpdateCacheInfo(_cacheInfo);

            PagedThread.Remove(_cacheInfo.Id);
        }
    }

    internal class CacheThread
    {
        public Guid Id { get; }

        public CacheThread()
        {
            Id = Guid.NewGuid();
        }

        private CacheProcess _process;

        public void Execute<T>(IDbConnection connection, int pageSize, string sqlCommandString, IDbDataParameter[] parameters, int commandTimeout) where T : new()
        {
            _process = new CacheProcess(Id, pageSize);

            _process.ExecuteForSql<T>(connection, sqlCommandString, parameters, commandTimeout);
        }

        public void WaitFristPaged()
        {
            while (!_process.IsReady)
            {
                //Waiting...
            }
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

    internal class CacheExpiredTime
    {
        public Guid Id { get; set; }
        public DateTime ExpiredTime { get; set; }
    }

    internal class CacheInfo
    {
        public Guid Id { get; set; }

        public bool IsComplate { get; set; }

        public int ProcessPage { get; set; }

        public int TotalPageCount { get; set; }

        public int TotalCount { get; set; }

        public int PageSize { get; set; }
    }

    #endregion
}