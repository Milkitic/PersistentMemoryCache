using PersistentMemoryCache.Internal;

namespace PersistentMemoryCache;

public interface IPersistentStore
{
    void AddOrUpdateEntry(LiteDbCacheEntry entry);
    LiteDbCacheEntry LoadEntryByKey(object key);
    void RemoveEntryByKey(object key);
    void RemoveEntry(int id);
}