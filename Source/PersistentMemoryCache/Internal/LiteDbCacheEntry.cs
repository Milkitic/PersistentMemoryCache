using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.Extensions.Caching.Memory;

namespace PersistentMemoryCache.Internal;

public abstract class LiteDbCacheEntry
{
    private static ConcurrentDictionary<Type, Delegate> _constructors = new();
    private static Type _liteDbCacheEntryOpenType = typeof(LiteDbCacheEntry<>);
    private static Type[] _emptyTypesArray = [];

    private TimeSpan? _slidingExpiration;

    public int Id { get; set; }
    public string CacheName { get; set; }
    public CacheItemPriority Priority { get; set; }
    public object Key { get; set; }
    internal DateTimeOffset LastAccessed { get; set; }
    public DateTimeOffset? AbsoluteExpiration { get; set; }
    public TimeSpan? SlidingExpiration
    {
        get
        {
            if (_slidingExpiration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(SlidingExpiration), _slidingExpiration, "The sliding expiration value must be positive.");
            }
            return _slidingExpiration;
        }
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(SlidingExpiration), value, "The sliding expiration value must be positive.");
            }
            _slidingExpiration = value;
        }
    }

    public static LiteDbCacheEntry ConstructCacheEntry(Type type) => (LiteDbCacheEntry)_constructors.GetOrAdd(type, cacheType =>
    {
        var cacheEntryClosedType = _liteDbCacheEntryOpenType.MakeGenericType(type);
        var constructor = cacheEntryClosedType.GetConstructor(_emptyTypesArray);
        var delegateType = typeof(Func<>).MakeGenericType(cacheEntryClosedType);
        var lambda = Expression.Lambda(delegateType, Expression.New(constructor));
        return lambda.Compile();
    }).DynamicInvoke();

    public abstract object GetValue();
    public abstract void SetValue(object value);
}

public class LiteDbCacheEntry<T> : LiteDbCacheEntry
{
    public T Value { get; set; }

    public override object GetValue() => Value;

    public override void SetValue(object value) => Value = (T)value;
}