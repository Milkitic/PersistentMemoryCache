using System;
using LiteDB;
using Microsoft.Extensions.Caching.Memory;

namespace PersistentMemoryCache;

/// <summary>
/// An implementation of <see cref="IMemoryCache"/> using a dictionary and IPersistentStore to
/// store its entries.
/// </summary>
public class PersistentMemoryCache : IMemoryCache
{
    private readonly MemoryCache _internalCache;
    private readonly IPersistentStore _store;
    private readonly string _cacheName;
    private readonly bool _isPersistent;

    /// <summary>
    /// Creates a new <see cref="PersistentMemoryCache"/> instance.
    /// </summary>
    /// <param name="options">The options of the cache.</param>
    public PersistentMemoryCache(PersistentMemoryCacheOptions options)
    {
        _cacheName = options.CacheName;
        _store = options.PersistentStore;
        _isPersistent = options.IsPersistent;
        _internalCache = new MemoryCache(options);
    }

    public ICacheEntry CreateEntry(object key)
    {
        var entry = _internalCache.CreateEntry(key);
        return new PersistentCacheEntryWrapper(entry, _store, _cacheName, _isPersistent);
    }

    public bool TryGetValue(object key, out object result)
    {
        if (_internalCache.TryGetValue(key, out result)) return true;

        if (!_isPersistent) return false;

        var dbEntry = _store.LoadEntryByKey(key);
        if (dbEntry == null || dbEntry.CacheName != _cacheName) return false;

        if (dbEntry.AbsoluteExpiration.HasValue && dbEntry.AbsoluteExpiration < DateTimeOffset.UtcNow)
        {
            _store.RemoveEntry(dbEntry.Id); // Remove expired entry
            return false;
        }

        object value = dbEntry.Value;
        if (!string.IsNullOrEmpty(dbEntry.DataType))
        {
            var type = Type.GetType(dbEntry.DataType);
            if (type != null)
            {
                value = BsonMapper.Global.Deserialize(type, dbEntry.Value);
            }
        }

        using (var entry = CreateEntry(key))
        {
            entry.AbsoluteExpiration = dbEntry.AbsoluteExpiration;
            entry.SlidingExpiration = dbEntry.SlidingExpiration;
            entry.Priority = dbEntry.Priority;
            entry.Value = value;
        }

        result = value;
        return true;
    }

    public void Remove(object key)
    {
        _internalCache.Remove(key);
        if (_isPersistent)
        {
            _store.RemoveEntryByKey(key);
        }
    }

    public void Dispose()
    {
        _internalCache.Dispose();
        if (_store is IDisposable disposableStore)
        {
            disposableStore.Dispose();
        }
    }
}