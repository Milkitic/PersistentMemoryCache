using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using LiteDB;
using PersistentMemoryCache.Internal;

namespace PersistentMemoryCache;

public class LiteDbStore : IPersistentStore, IDisposable
{
    private const string CollectionName = "PersistedCacheEntry";
    private readonly PersistentLiteDatabase _db;
    private readonly ILiteCollection<LiteDbCacheEntry> _collection;
    private readonly BlockingCollection<Action> _writeQueue = new BlockingCollection<Action>();
    private readonly Task _backgroundTask;

    public LiteDbStore(LiteDbOptions options)
    {
        var connectionString = $"filename={options.FileName};upgrade=true";
        _db = new PersistentLiteDatabase(connectionString);
        _collection = _db.GetCollection<LiteDbCacheEntry>(CollectionName);
        
        _collection.EnsureIndex(pce => pce.CacheName);
        _collection.EnsureIndex(pce => pce.Key);

        _backgroundTask = Task.Factory.StartNew(ProcessQueue, TaskCreationOptions.LongRunning);
    }

    private void ProcessQueue()
    {
        foreach (var action in _writeQueue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch
            {
                // Ignore errors in background thread to prevent crash
            }
        }
    }

    public LiteDbCacheEntry LoadEntryByKey(object key)
    {
        return _collection.FindOne(x => x.Key == key);
    }

    public void RemoveEntryByKey(object key)
    {
        _writeQueue.Add(() => 
        {
            _collection.DeleteMany(x => x.Key == key);
        });
    }

    public void AddOrUpdateEntry(LiteDbCacheEntry entry)
    {
        _writeQueue.Add(() =>
        {
            var existing = _collection.FindOne(x => x.Key == entry.Key && x.CacheName == entry.CacheName);
            if (existing != null)
            {
                entry.Id = existing.Id;
                _collection.Update(entry);
            }
            else
            {
                _collection.Insert(entry);
            }
        });
    }

    public void RemoveEntry(int id)
    {
        _writeQueue.Add(() =>
        {
            _collection.Delete(new BsonValue(id));
        });
    }

    public void Dispose()
    {
        _writeQueue.CompleteAdding();
        try
        {
            _backgroundTask.Wait(5000);
        }
        catch
        {
            // Ignore wait errors
        }
        _db.Dispose();
    }
}