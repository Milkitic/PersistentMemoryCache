using System.Collections.Generic;
using PersistentMemoryCache.Internal;

namespace PersistentMemoryCache;

public interface IPersistentStore
{
    int AddEntry(LiteDbCacheEntry entry);
    LiteDbCacheEntry LoadEntry(int key);
    List<LiteDbCacheEntry> LoadEntries(string cacheName);
    void RemoveEntry(int id);
    bool UpdateEntry(int key, LiteDbCacheEntry entry);
}