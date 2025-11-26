using Microsoft.Extensions.Caching.Memory;

namespace PersistentMemoryCache;

public class PersistentMemoryCacheOptions : MemoryCacheOptions
{
    public PersistentMemoryCacheOptions(string cacheName, IPersistentStore persistentStore)
    {
        CacheName = cacheName;
        PersistentStore = persistentStore;
    }

    public string CacheName { get; }
    public IPersistentStore PersistentStore { get; }
    public bool IsPersistent { get; set; } = true;
}