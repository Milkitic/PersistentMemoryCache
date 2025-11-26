using Microsoft.Extensions.Caching.Memory;

namespace PersistentMemoryCache;

public class PersistentMemoryCacheOptions(string cacheName, IPersistentStore persistentStore) : MemoryCacheOptions
{
    public string CacheName { get; } = cacheName;
    public IPersistentStore PersistentStore { get; } = persistentStore;
    public bool IsPersistent { get; set; } = true;
}