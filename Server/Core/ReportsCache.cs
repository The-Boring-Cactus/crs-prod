using System.Collections.Concurrent;
using System.Text.Json;

namespace Server.Core;

public class ReportsCache : IDisposable
{
    // Program.cs constructs exactly one ReportsCache for the process's lifetime and injects
    // it into WebSocketManager/DataSourceManager via DI. Everything else that wants to share
    // that same cache (HeadlessQueryExecutor, and through it ScheduledRefreshService and
    // PublicController) is a static class with no DI, so it reaches the same instance here
    // instead. Safe because only one ReportsCache is ever constructed.
    public static ReportsCache Instance { get; private set; }

    private readonly ConcurrentDictionary<string, byte[]> _store;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly CacheMetrics _metrics;

    public ReportsCache(string cacheName)
    {
        _store = new ConcurrentDictionary<string, byte[]>();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        _metrics = new CacheMetrics();
        Instance = this;
    }
    
    public async Task<T> GetOrExecuteAsync<T>(
        string queryKey, 
        Func<Task<T>> queryExecutor, 
        TimeSpan maxAge)
    {
        var startTime = DateTime.UtcNow;
        var cacheEntry = GetCacheEntry<T>(queryKey);
        
        if (cacheEntry != null && !IsExpired(cacheEntry.Timestamp, maxAge))
        {
            _metrics.CacheHits++;
            _metrics.UpdateAverageQueryTime(DateTime.UtcNow - startTime);
            return cacheEntry.Data;
        }
        
        _metrics.CacheMisses++;
        var freshData = await queryExecutor();
        StoreCacheEntry(queryKey, freshData);
        _metrics.UpdateAverageQueryTime(DateTime.UtcNow - startTime);
        
        return freshData;
    }
    
    private CacheEntry<T> GetCacheEntry<T>(string key)
    {
        var entryKey = $"entry:{key}";
        if (!_store.TryGetValue(entryKey, out var json) || json == null)
            return null;
        
        try 
        {
            var jsonString = System.Text.Encoding.UTF8.GetString(json);
            return JsonSerializer.Deserialize<CacheEntry<T>>(jsonString, _jsonOptions);
        }
        catch
        {
            _store.TryRemove(entryKey, out _);
            return null;
        }
    }
    
    private void StoreCacheEntry<T>(string key, T data)
    {
        var entry = new CacheEntry<T>
        {
            Data = data,
            Timestamp = DateTimeOffset.UtcNow,
            Key = key
        };
        
        var json = JsonSerializer.Serialize(entry, _jsonOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        
        _store[$"entry:{key}"] = bytes;
        _store[$"timestamp:{key}"] = System.Text.Encoding.UTF8.GetBytes(entry.Timestamp.ToUnixTimeMilliseconds().ToString());
    }
    
    private bool IsExpired(DateTimeOffset timestamp, TimeSpan maxAge)
    {
        return DateTimeOffset.UtcNow - timestamp > maxAge;
    }
    
    public void InvalidateQuery(string queryKey)
    {
        _store.TryRemove($"entry:{queryKey}", out _);
        _store.TryRemove($"timestamp:{queryKey}", out _);
    }
    
    public CacheMetrics GetMetrics()
    {
        return _metrics;
    }
    
    public void Dispose()
    {
        _store.Clear();
    }
}