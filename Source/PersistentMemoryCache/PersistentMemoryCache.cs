//Based on https://github.com/aspnet/Caching/blob/dev/src/Microsoft.Extensions.Caching.Memory/MemoryCache.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using PersistentMemoryCache.Internal;

namespace PersistentMemoryCache;

/// <summary>
/// An implementation of <see cref="IMemoryCache"/> using a dictionary and IPersistentStore to
/// store its entries.
/// </summary>
public class PersistentMemoryCache : IMemoryCache
{
    private Dictionary<object, PersistentCacheEntry> _inMemoryEntries;
    private readonly ReaderWriterLockSlim _entryLock;
    private bool _disposed;

    // We store the delegates locally to prevent allocations
    // every time a new CacheEntry is created.
    private readonly Action<PersistentCacheEntry> _setEntry;
    private readonly Action<PersistentCacheEntry> _entryExpirationNotification;

    private DateTimeOffset _lastExpirationScan;
    private bool _isReloadingFromStore;
    private PersistentMemoryCacheOptions _options;

    /// <summary>
    /// Creates a new <see cref="PersistentMemoryCache"/> instance.
    /// </summary>
    /// <param name="options">The options of the cache.</param>
    public PersistentMemoryCache(PersistentMemoryCacheOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _options = options;

        _inMemoryEntries = new Dictionary<object, PersistentCacheEntry>();
        _entryLock = new ReaderWriterLockSlim();
        _setEntry = SetEntry;
        _entryExpirationNotification = EntryExpired;

        _lastExpirationScan = _options.Clock.UtcNow;
        if (_options.CompactOnMemoryPressure)
        {
            GcNotification.Register(DoMemoryPreassureCollection, state: null);
        }

        if (_options.IsPersistent)
        {
            ReloadDataFromStore();
        }
    }

    private void ReloadDataFromStore()
    {
        try
        {
            _isReloadingFromStore = true;
            List<LiteDbCacheEntry> persistentCacheEntries = _options.PersistentStore.LoadEntries(_options.CacheName);
            foreach (LiteDbCacheEntry persistentCacheEntry in persistentCacheEntries)
            {
                using PersistentCacheEntry cacheEntry = (PersistentCacheEntry)CreateEntry(persistentCacheEntry.Key);
                cacheEntry.PersistentStoreId = persistentCacheEntry.Id;
                cacheEntry.Priority = persistentCacheEntry.Priority;
                cacheEntry.Value = persistentCacheEntry.GetValue();
                cacheEntry.LastAccessed = persistentCacheEntry.LastAccessed;
                cacheEntry.AbsoluteExpiration = persistentCacheEntry.AbsoluteExpiration;
                cacheEntry.SlidingExpiration = persistentCacheEntry.SlidingExpiration;
            }
        }
        finally
        {
            _isReloadingFromStore = false;
        }
    }

    /// <summary>
    /// Cleans up the background collection events.
    /// </summary>
    ~PersistentMemoryCache()
    {
        Dispose(false);
    }

    /// <summary>
    /// Gets the count of the current entries for diagnostic purposes.
    /// </summary>
    public int Count => _inMemoryEntries.Count;

    public ICacheEntry CreateEntry(object key)
    {
        CheckDisposed();
        return new PersistentCacheEntry(key: key, notifyCacheEntryDisposed: _setEntry,
            notifyCacheOfExpiration: _entryExpirationNotification);
    }

    private void SetEntry(PersistentCacheEntry entry)
    {
        var utcNow = _options.Clock.UtcNow;

        DateTimeOffset? absoluteExpiration = null;
        if (entry.FieldAbsoluteExpirationRelativeToNow.HasValue)
        {
            absoluteExpiration = utcNow + entry.FieldAbsoluteExpirationRelativeToNow;
        }
        else if (entry.FieldAbsoluteExpiration.HasValue)
        {
            absoluteExpiration = entry.FieldAbsoluteExpiration;
        }

        // Applying the option's absolute expiration only if it's not already smaller.
        // This can be the case if a dependent cache entry has a smaller value, and
        // it was set by cascading it to its parent.
        if (absoluteExpiration.HasValue)
        {
            if (!entry.FieldAbsoluteExpiration.HasValue || absoluteExpiration.Value < entry.FieldAbsoluteExpiration.Value)
            {
                entry.FieldAbsoluteExpiration = absoluteExpiration;
            }
        }

        // Initialize the last access timestamp at the time the entry is added
        entry.LastAccessed = utcNow;

        var added = false;
        PersistentCacheEntry priorEntry;

        _entryLock.EnterWriteLock();
        try
        {
            if (_inMemoryEntries.TryGetValue(entry.Key, out priorEntry))
            {
                RemoveEntryFromMemoryAndStore(priorEntry);
                priorEntry.SetExpired(EvictionReason.Replaced);
            }

            if (!entry.CheckExpired(utcNow))
            {
                AddEntryToMemoryAndStore(entry);
                entry.AttachTokens();
                if (_options.IsPersistent)
                {
                    entry.PropertyChanged += Entry_PropertyChanged;
                }

                added = true;
            }
        }
        finally
        {
            _entryLock.ExitWriteLock();
        }

        if (priorEntry != null)
        {
            priorEntry.InvokeEvictionCallbacks();
        }

        if (!added)
        {
            entry.InvokeEvictionCallbacks();
        }

        StartScanForExpiredItems();
    }

    private void Entry_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var entry = (PersistentCacheEntry)sender;
        var liteDbEntry = CreateLiteDbEntryFromPersistentEntry(entry);
        _options.PersistentStore.UpdateEntry(entry.PersistentStoreId.Value, liteDbEntry);
    }

    private void AddEntryToMemoryAndStore(PersistentCacheEntry entry)
    {
        _inMemoryEntries[entry.Key] = entry;
        if (_options.IsPersistent && !_isReloadingFromStore)
        {
            LiteDbCacheEntry liteDbEntry = CreateLiteDbEntryFromPersistentEntry(entry);
            entry.PersistentStoreId = _options.PersistentStore.AddEntry(liteDbEntry);
        }
    }

    private LiteDbCacheEntry CreateLiteDbEntryFromPersistentEntry(PersistentCacheEntry entry)
    {
        Type cacheValueType = entry.Value?.GetType() ?? typeof(object);
        LiteDbCacheEntry liteDbEntry = LiteDbCacheEntry.ConstructCacheEntry(cacheValueType);
        liteDbEntry.CacheName = _options.CacheName;
        liteDbEntry.Priority = entry.Priority;
        liteDbEntry.Key = entry.Key;
        liteDbEntry.LastAccessed = entry.LastAccessed;
        liteDbEntry.AbsoluteExpiration = entry.AbsoluteExpiration;
        liteDbEntry.SlidingExpiration = entry.SlidingExpiration;
        liteDbEntry.SetValue(entry.Value);
        return liteDbEntry;
    }

    public bool TryGetValue(object key, out object result)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        var utcNow = _options.Clock.UtcNow;
        result = null;
        bool found = false;
        PersistentCacheEntry expiredEntry = null;
        CheckDisposed();
        _entryLock.EnterReadLock();
        try
        {
            PersistentCacheEntry entry;
            if (_inMemoryEntries.TryGetValue(key, out entry))
            {
                // Check if expired due to expiration tokens, timers, etc. and if so, remove it.
                if (entry.CheckExpired(utcNow))
                {
                    expiredEntry = entry;
                }
                else
                {
                    found = true;
                    entry.LastAccessed = utcNow;
                    result = entry.Value;
                }
            }
        }
        finally
        {
            _entryLock.ExitReadLock();
        }

        if (expiredEntry != null)
        {
            // TODO: For efficiency queue this up for batch removal
            RemoveEntry(expiredEntry);
        }

        StartScanForExpiredItems();

        return found;
    }

    public void Remove(object key)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        CheckDisposed();
        PersistentCacheEntry entry;
        _entryLock.EnterReadLock();
        try
        {
            if (_inMemoryEntries.TryGetValue(key, out entry))
            {
                entry.SetExpired(EvictionReason.Removed);
            }
        }
        finally
        {
            _entryLock.ExitReadLock();
        }

        if (entry != null)
        {
            // TODO: For efficiency consider processing these removals in batches.
            RemoveEntry(entry);
        }

        StartScanForExpiredItems();
    }

    private void RemoveEntry(PersistentCacheEntry entry)
    {
        _entryLock.EnterWriteLock();
        try
        {
            // Only remove it if someone hasn't modified it since our lookup
            PersistentCacheEntry currentEntry;
            if (_inMemoryEntries.TryGetValue(entry.Key, out currentEntry)
                && ReferenceEquals(currentEntry, entry))
            {
                RemoveEntryFromMemoryAndStore(entry);
                if (_options.IsPersistent)
                {
                    entry.PropertyChanged -= Entry_PropertyChanged;
                }
            }
        }
        finally
        {
            _entryLock.ExitWriteLock();
        }

        entry.InvokeEvictionCallbacks();
    }

    private void RemoveEntryFromMemoryAndStore(PersistentCacheEntry entry)
    {
        _inMemoryEntries.Remove(entry.Key);
        if (_options.IsPersistent)
        {
            _options.PersistentStore.RemoveEntry(entry.PersistentStoreId.Value);
        }
    }

    private void RemoveEntries(List<PersistentCacheEntry> entries)
    {
        _entryLock.EnterWriteLock();
        try
        {
            foreach (var entry in entries)
            {
                // Only remove it if someone hasn't modified it since our lookup
                PersistentCacheEntry currentEntry;
                if (_inMemoryEntries.TryGetValue(entry.Key, out currentEntry) && ReferenceEquals(currentEntry, entry))
                {
                    RemoveEntryFromMemoryAndStore(entry);
                }
            }
        }
        finally
        {
            _entryLock.ExitWriteLock();
        }

        foreach (var entry in entries)
        {
            entry.InvokeEvictionCallbacks();
        }
    }

    internal void Clear()
    {
        RemoveEntries(_inMemoryEntries.Values.ToList());
    }

    private void EntryExpired(PersistentCacheEntry entry)
    {
        // TODO: For efficiency consider processing these expirations in batches.
        RemoveEntry(entry);
        StartScanForExpiredItems();
    }

    // Called by multiple actions to see how long it's been since we last checked for expired items.
    // If sufficient time has elapsed then a scan is initiated on a background task.
    private void StartScanForExpiredItems()
    {
        var now = _options.Clock.UtcNow;
        if (_options.ExpirationScanFrequency < now - _lastExpirationScan)
        {
            _lastExpirationScan = now;
            Task.Factory.StartNew(state => ScanForExpiredItems((PersistentMemoryCache)state), this,
                CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }
    }

    private static void ScanForExpiredItems(PersistentMemoryCache cache)
    {
        List<PersistentCacheEntry> expiredEntries = [];

        cache._entryLock.EnterReadLock();
        try
        {
            var now = cache._options.Clock.UtcNow;
            foreach (var entry in cache._inMemoryEntries.Values)
            {
                if (entry.CheckExpired(now))
                {
                    expiredEntries.Add(entry);
                }
            }
        }
        finally
        {
            cache._entryLock.ExitReadLock();
        }

        cache.RemoveEntries(expiredEntries);
    }

    /// This is called after a Gen2 garbage collection. We assume this means there was memory pressure.
    /// Remove at least 10% of the total entries (or estimated memory?).
    private bool DoMemoryPreassureCollection(object state)
    {
        if (_disposed)
        {
            return false;
        }

        Compact(0.10);

        return true;
    }

    /// Remove at least the given percentage (0.10 for 10%) of the total entries (or estimated memory?), according to the following policy:
    /// 1. Remove all expired items.
    /// 2. Bucket by CacheItemPriority.
    /// ?. Least recently used objects.
    /// ?. Items with the soonest absolute expiration.
    /// ?. Items with the soonest sliding expiration.
    /// ?. Larger objects - estimated by object graph size, inaccurate.
    public void Compact(double percentage)
    {
        List<PersistentCacheEntry> expiredEntries = [];
        List<PersistentCacheEntry> lowPriEntries = [];
        List<PersistentCacheEntry> normalPriEntries = [];
        List<PersistentCacheEntry> highPriEntries = [];
        List<PersistentCacheEntry> neverRemovePriEntries = [];

        _entryLock.EnterReadLock();
        try
        {
            // Sort items by expired & priority status
            var now = _options.Clock.UtcNow;
            foreach (var entry in _inMemoryEntries.Values)
            {
                if (entry.CheckExpired(now))
                {
                    expiredEntries.Add(entry);
                }
                else
                {
                    switch (entry.Priority)
                    {
                        case CacheItemPriority.Low:
                            lowPriEntries.Add(entry);
                            break;
                        case CacheItemPriority.Normal:
                            normalPriEntries.Add(entry);
                            break;
                        case CacheItemPriority.High:
                            highPriEntries.Add(entry);
                            break;
                        case CacheItemPriority.NeverRemove:
                            neverRemovePriEntries.Add(entry);
                            break;
                        default:
                            System.Diagnostics.Debug.Assert(false, "Not implemented: " + entry.Priority);
                            break;
                    }
                }
            }

            int totalEntries = expiredEntries.Count + lowPriEntries.Count + normalPriEntries.Count +
                               highPriEntries.Count + neverRemovePriEntries.Count;
            int removalCountTarget = (int)(totalEntries * percentage);

            ExpirePriorityBucket(removalCountTarget, expiredEntries, lowPriEntries);
            ExpirePriorityBucket(removalCountTarget, expiredEntries, normalPriEntries);
            ExpirePriorityBucket(removalCountTarget, expiredEntries, highPriEntries);
        }
        finally
        {
            _entryLock.ExitReadLock();
        }

        RemoveEntries(expiredEntries);
    }

    /// Policy:
    /// ?. Least recently used objects.
    /// ?. Items with the soonest absolute expiration.
    /// ?. Items with the soonest sliding expiration.
    /// ?. Larger objects - estimated by object graph size, inaccurate.
    private void ExpirePriorityBucket(int removalCountTarget, List<PersistentCacheEntry> expiredEntries,
        List<PersistentCacheEntry> priorityEntries)
    {
        // Do we meet our quota by just removing expired entries?
        if (removalCountTarget <= expiredEntries.Count)
        {
            // No-op, we've met quota
            return;
        }

        if (expiredEntries.Count + priorityEntries.Count <= removalCountTarget)
        {
            // Expire all of the entries in this bucket
            foreach (var entry in priorityEntries)
            {
                entry.SetExpired(EvictionReason.Capacity);
            }

            expiredEntries.AddRange(priorityEntries);
            return;
        }

        // Expire enough entries to reach our goal
        // TODO: Refine policy

        // LRU
        foreach (var entry in priorityEntries.OrderBy(entry => entry.LastAccessed))
        {
            entry.SetExpired(EvictionReason.Capacity);
            expiredEntries.Add(entry);
            if (removalCountTarget <= expiredEntries.Count)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                GC.SuppressFinalize(this);
            }

            _disposed = true;
        }
    }

    private void CheckDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(typeof(PersistentMemoryCache).FullName);
        }
    }
}