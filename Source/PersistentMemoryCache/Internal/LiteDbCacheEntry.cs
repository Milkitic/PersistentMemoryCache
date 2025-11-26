using System;
using LiteDB;
using Microsoft.Extensions.Caching.Memory;

namespace PersistentMemoryCache.Internal;

public class LiteDbCacheEntry
{
    public int Id { get; set; }
    public string CacheName { get; set; }
    public CacheItemPriority Priority { get; set; }
    public object Key { get; set; }
    public DateTimeOffset? AbsoluteExpiration { get; set; }
    public string DataType { get; set; }
    public BsonValue Value { get; set; }
    internal DateTimeOffset LastAccessed { get; set; }

    public TimeSpan? SlidingExpiration
    {
        get
        {
            if (field <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(SlidingExpiration), field,
                    "The sliding expiration value must be positive.");
            }

            return field;
        }
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(SlidingExpiration), value,
                    "The sliding expiration value must be positive.");
            }

            field = value;
        }
    }
}