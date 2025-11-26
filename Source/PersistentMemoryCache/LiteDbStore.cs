using LiteDB;
using PersistentMemoryCache.Internal;

namespace PersistentMemoryCache;

public class LiteDbStore : IPersistentStore
{
    private const string CollectionName = "PersistedCacheEntry";
    private readonly string _connectionString;

    public LiteDbStore(LiteDbOptions options)
    {
        _connectionString = $"filename={options.FileName};upgrade=true";
        using var db = new PersistentLiteDatabase(_connectionString);
        var collection = db.GetCollection<LiteDbCacheEntry>(CollectionName);
        collection.EnsureIndex(pce => pce.CacheName);
    }

    public LiteDbCacheEntry LoadEntryByKey(object key)
    {
        using var db = new PersistentLiteDatabase(_connectionString);
        var collection = db.GetCollection<LiteDbCacheEntry>(CollectionName);
        return collection.FindOne(x => x.Key == key);
    }

    public void RemoveEntryByKey(object key)
    {
        using var db = new PersistentLiteDatabase(_connectionString);
        var collection = db.GetCollection<LiteDbCacheEntry>(CollectionName);
        collection.DeleteMany(x => x.Key == key);
    }

    public void AddOrUpdateEntry(LiteDbCacheEntry entry)
    {
        using var db = new PersistentLiteDatabase(_connectionString);
        var collection = db.GetCollection<LiteDbCacheEntry>(CollectionName);

        var existing = collection.FindOne(x => x.Key == entry.Key && x.CacheName == entry.CacheName);
        if (existing != null)
        {
            entry.Id = existing.Id;
            collection.Update(entry);
        }
        else
        {
            collection.Insert(entry);
        }
    }

    public void RemoveEntry(int id)
    {
        using var db = new LiteDatabase(_connectionString);
        var collection = db.GetCollection<LiteDbCacheEntry>(CollectionName);
        collection.Delete(new BsonValue(id));
    }
}