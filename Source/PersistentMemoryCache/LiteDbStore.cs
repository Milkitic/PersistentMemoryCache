using System.Collections.Generic;
using System.Linq;
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

    public int AddEntry(LiteDbCacheEntry entry)
    {
        using var db = new PersistentLiteDatabase(_connectionString);
        var collection = db.GetCollection<LiteDbCacheEntry>(CollectionName);
        return collection.Insert(entry).AsInt32;
    }

    public List<LiteDbCacheEntry> LoadEntries(string cacheName)
    {
        using var db = new PersistentLiteDatabase(_connectionString);
        var collection = db.GetCollection<LiteDbCacheEntry>(CollectionName);
        return collection.Find(pce => pce.CacheName == cacheName).ToList();
    }

    public LiteDbCacheEntry LoadEntry(int key)
    {
        using var db = new PersistentLiteDatabase(_connectionString);
        var collection = db.GetCollection<LiteDbCacheEntry>(CollectionName);
        return collection.FindById(new BsonValue(key));
    }

    public bool UpdateEntry(int key, LiteDbCacheEntry entry)
    {
        using var db = new PersistentLiteDatabase(_connectionString);
        var collection = db.GetCollection<LiteDbCacheEntry>(CollectionName);
        return collection.Update(new BsonValue(key), entry);
    }

    public void RemoveEntry(int id)
    {
        using var db = new LiteDatabase(_connectionString);
        var collection = db.GetCollection<LiteDbCacheEntry>(CollectionName);
        collection.Delete(new BsonValue(id));
    }
}