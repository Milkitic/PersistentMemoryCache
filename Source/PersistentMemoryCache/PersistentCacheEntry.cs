using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace PersistentMemoryCache;

public class PersistentCacheEntry : ICacheEntry, INotifyPropertyChanged
{
    private bool _added;
    private static readonly Action<object> ExpirationCallback = ExpirationTokensExpired;
    private readonly Action<PersistentCacheEntry> _notifyCacheOfExpiration;
    private readonly Action<PersistentCacheEntry> _notifyCacheEntryDisposed;
    private IList<IDisposable> _expirationTokenRegistrations;
    private EvictionReason _evictionReason;
    private IList<PostEvictionCallbackRegistration> _postEvictionCallbacks;
    private bool _isExpired;

    private IList<IChangeToken> _expirationTokens;
    private readonly object _lock = new();
    internal DateTimeOffset? FieldAbsoluteExpiration;
    internal TimeSpan? FieldAbsoluteExpirationRelativeToNow;

    private TimeSpan? _slidingExpiration;
    private DateTimeOffset _lastAccessed;
    private object _value;
    private CacheItemPriority _priority;

    internal PersistentCacheEntry(object key, Action<PersistentCacheEntry> notifyCacheEntryDisposed,
        Action<PersistentCacheEntry> notifyCacheOfExpiration)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        _notifyCacheEntryDisposed =
            notifyCacheEntryDisposed ?? throw new ArgumentNullException(nameof(notifyCacheEntryDisposed));
        _notifyCacheOfExpiration =
            notifyCacheOfExpiration ?? throw new ArgumentNullException(nameof(notifyCacheOfExpiration));

        Priority = CacheItemPriority.Normal;
    }

    /// <summary>
    /// Gets or sets an absolute expiration date for the cache entry.
    /// </summary>
    public DateTimeOffset? AbsoluteExpiration
    {
        get => FieldAbsoluteExpiration;
        set => SetValue(value, ref FieldAbsoluteExpiration);
    }

    /// <summary>
    /// Gets or sets an absolute expiration time, relative to now.
    /// </summary>
    public TimeSpan? AbsoluteExpirationRelativeToNow
    {
        get => FieldAbsoluteExpirationRelativeToNow;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(AbsoluteExpirationRelativeToNow), value,
                    "The relative expiration value must be positive.");
            }

            SetValue(value, ref FieldAbsoluteExpirationRelativeToNow);
        }
    }

    /// <summary>
    /// Gets or sets how long a cache entry can be inactive (e.g. not accessed) before it will be removed.
    /// This will not extend the entry lifetime beyond the absolute expiration (if set).
    /// </summary>
    public TimeSpan? SlidingExpiration
    {
        get => _slidingExpiration;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(SlidingExpiration),
                    value,
                    "The sliding expiration value must be positive.");
            }

            SetValue(value, ref _slidingExpiration);
        }
    }

    /// <summary>
    /// Gets the <see cref="IChangeToken"/> instances which cause the cache entry to expire.
    /// </summary>
    public IList<IChangeToken> ExpirationTokens
    {
        get
        {
            if (_expirationTokens == null)
            {
                _expirationTokens = new List<IChangeToken>();
            }

            return _expirationTokens;
        }
    }

    /// <summary>
    /// Gets or sets the callbacks will be fired after the cache entry is evicted from the cache.
    /// </summary>
    public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks
    {
        get
        {
            if (_postEvictionCallbacks == null)
            {
                _postEvictionCallbacks = new List<PostEvictionCallbackRegistration>();
            }

            return _postEvictionCallbacks;
        }
    }

    /// <summary>
    /// Gets or sets the priority for keeping the cache entry in the cache during a
    /// memory pressure triggered cleanup. The default is <see cref="CacheItemPriority.Normal"/>.
    /// </summary>
    public CacheItemPriority Priority
    {
        get => _priority;
        set => SetValue(value, ref _priority);
    }

    public object Key { get; private set; }

    public object Value
    {
        get => _value;
        set => SetValue(value, ref _value);
    }

    internal DateTimeOffset LastAccessed
    {
        get => _lastAccessed;

        set => SetValue(value, ref _lastAccessed);
    }

    internal int? PersistentStoreId { get; set; }

    public long? Size { get; set; }

    public void Dispose()
    {
        if (!_added)
        {
            _added = true;
            _notifyCacheEntryDisposed(this);
        }
    }

    internal bool CheckExpired(DateTimeOffset now)
    {
        return _isExpired || CheckForExpiredTime(now) || CheckForExpiredTokens();
    }

    internal void SetExpired(EvictionReason reason)
    {
        _isExpired = true;
        if (_evictionReason == EvictionReason.None)
        {
            _evictionReason = reason;
        }

        DetachTokens();
    }

    private bool CheckForExpiredTime(DateTimeOffset now)
    {
        if (FieldAbsoluteExpiration.HasValue && FieldAbsoluteExpiration.Value <= now)
        {
            SetExpired(EvictionReason.Expired);
            return true;
        }

        if (_slidingExpiration.HasValue && (now - LastAccessed) >= _slidingExpiration)
        {
            SetExpired(EvictionReason.Expired);
            return true;
        }

        return false;
    }

    internal bool CheckForExpiredTokens()
    {
        if (_expirationTokens != null)
        {
            for (int i = 0; i < _expirationTokens.Count; i++)
            {
                var expiredToken = _expirationTokens[i];
                if (expiredToken.HasChanged)
                {
                    SetExpired(EvictionReason.TokenExpired);
                    return true;
                }
            }
        }

        return false;
    }

    internal void AttachTokens()
    {
        if (_expirationTokens != null)
        {
            lock (_lock)
            {
                for (int i = 0; i < _expirationTokens.Count; i++)
                {
                    var expirationToken = _expirationTokens[i];
                    if (expirationToken.ActiveChangeCallbacks)
                    {
                        if (_expirationTokenRegistrations == null)
                        {
                            _expirationTokenRegistrations = new List<IDisposable>(1);
                        }

                        var registration = expirationToken.RegisterChangeCallback(ExpirationCallback, this);
                        _expirationTokenRegistrations.Add(registration);
                    }
                }
            }
        }
    }

    private static void ExpirationTokensExpired(object obj)
    {
        // start a new thread to avoid issues with callbacks called from RegisterChangeCallback
        Task.Factory.StartNew(state =>
        {
            var entry = (PersistentCacheEntry)state;
            entry.SetExpired(EvictionReason.TokenExpired);
            entry._notifyCacheOfExpiration(entry);
        }, obj, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
    }

    private void DetachTokens()
    {
        lock (_lock)
        {
            var registrations = _expirationTokenRegistrations;
            if (registrations != null)
            {
                _expirationTokenRegistrations = null;
                for (int i = 0; i < registrations.Count; i++)
                {
                    var registration = registrations[i];
                    registration.Dispose();
                }
            }
        }
    }

    internal void InvokeEvictionCallbacks()
    {
        if (_postEvictionCallbacks != null)
        {
            Task.Factory.StartNew(state => InvokeCallbacks((PersistentCacheEntry)state), this, CancellationToken.None,
                TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }
    }

    private static void InvokeCallbacks(PersistentCacheEntry entry)
    {
        var callbackRegistrations = Interlocked.Exchange(ref entry._postEvictionCallbacks, null);

        if (callbackRegistrations == null)
        {
            return;
        }

        for (int i = 0; i < callbackRegistrations.Count; i++)
        {
            var registration = callbackRegistrations[i];

            try
            {
                registration.EvictionCallback?.Invoke(entry.Key, entry.Value, entry._evictionReason,
                    registration.State);
            }
            catch (Exception)
            {
                return;
                // This will be invoked on a background thread, don't let it throw.
                // TODO: LOG
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetValue<T>(T value, ref T field, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}