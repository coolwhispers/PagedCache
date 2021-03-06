# PagedCache

Use memory cache page

Nuget package:
```
Install-Package 
```

Model Sample:
```cs
using PagedCache;

var models = new List<ResultModel>();

// model paging
var result = models.ExecuteCache(pageSize);

```

DB Connection Sample:
```cs
using PagedCache;

var connection = new SqlConnection(conectionString);

var sqlCommandString = "SELECT * FROM SampleTable WHERE CreateTime < @Now ";

var parameters = new List<IDbDataParameter>();

parameters.Add(new SqlParameter("@Now", DateTime.Now));

var pageSize = 10;

// sql paging
var result = connection.ExecuteCache<ResultModel>(pageSize, sqlCommandStirng, parameters.ToArray());

```

Next Page Sample:
```cs

foreach(ResultModel item in result.Data)
{
    //...
}

//get next page
result = PagedCache.Next<ResultModel>(result.Next);

if(string.IsNullOrEmtpy(result.Next))
{
    //no data
}
```

Or use cache id
```cs
var cacheId = PagedCache.GetCacheId(result.Next);

//get page 2
result = PagedCache.Next<ResultModel>(cacheId, 2);
```
