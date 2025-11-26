using System;
using System.Collections.Generic;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using PersistentMemoryCache.Internal;

namespace PersistentMemoryCache;

public class PersistentCacheEntryWrapper : ICacheEntry
{
    private readonly ICacheEntry _inner;
    private readonly IPersistentStore _store;
    private readonly string _cacheName;
    private readonly bool _isPersistent;

    public PersistentCacheEntryWrapper(ICacheEntry inner, IPersistentStore store, string cacheName, bool isPersistent)
    {
        _inner = inner;
        _store = store;
        _cacheName = cacheName;
        _isPersistent = isPersistent;
        _inner.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration { EvictionCallback = OnEvicted });
    }

    public void Dispose()
    {
        if (_isPersistent && Value != null)
        {
            var liteDbEntry = new LiteDbCacheEntry<object>
            {
                CacheName = _cacheName,
                Key = Key,
                Value = Value,
                AbsoluteExpiration = AbsoluteExpiration,
                SlidingExpiration = SlidingExpiration,
                Priority = Priority,
                LastAccessed = DateTimeOffset.UtcNow
            };

            _store.AddOrUpdateEntry(liteDbEntry);
        }

        _inner.Dispose();
    }

    private void OnEvicted(object key, object value, EvictionReason reason, object state)
    {
        if (!_isPersistent) return;

        if (reason == EvictionReason.Capacity)
        {
            return; // 仅仅从内存消失，磁盘里还有
        }

        _store.RemoveEntryByKey(key);
    }

    public object Key => _inner.Key;

    public object Value
    {
        get => _inner.Value;
        set => _inner.Value = value;
    }

    public DateTimeOffset? AbsoluteExpiration
    {
        get => _inner.AbsoluteExpiration;
        set => _inner.AbsoluteExpiration = value;
    }

    public TimeSpan? AbsoluteExpirationRelativeToNow
    {
        get => _inner.AbsoluteExpirationRelativeToNow;
        set => _inner.AbsoluteExpirationRelativeToNow = value;
    }

    public TimeSpan? SlidingExpiration
    {
        get => _inner.SlidingExpiration;
        set => _inner.SlidingExpiration = value;
    }

    public IList<IChangeToken> ExpirationTokens => _inner.ExpirationTokens;
    public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => _inner.PostEvictionCallbacks;

    public CacheItemPriority Priority
    {
        get => _inner.Priority;
        set => _inner.Priority = value;
    }

    public long? Size
    {
        get => _inner.Size;
        set => _inner.Size = value;
    }
}