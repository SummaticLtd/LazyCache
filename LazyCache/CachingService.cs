using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace LazyCache
{
    /// <summary>
    ///     An in-memory cache whose GetOrAdd methods run the factory for a key only once, even under concurrent calls.
    /// </summary>
    public class CachingService
    {
        private readonly IMemoryCache cache = new MemoryCache(new MemoryCacheOptions());

        private readonly int[] keyLocks = new int[Math.Max(Environment.ProcessorCount * 8, 32)];

        /// <summary>
        ///     Adds an item to the cache, replacing any item under the key.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="item"/> is null.</exception>
        public void Add<T>(string key, T item, MemoryCacheEntryOptions policy)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            cache.Set(key, item, policy);
        }

        /// <summary>
        ///     Gets an item stored with <see cref="Add{T}"/>.
        /// </summary>
        public bool TryGetValue<T>(string key, out T value)
        {
            return cache.TryGetValue(key, out value);
        }

        /// <summary>
        ///     Removes the item under the key.
        /// </summary>
        public void Remove(string key)
        {
            cache.Remove(key);
        }

        /// <summary>
        ///     Gets the item under the key, or adds the result of the factory. A factory that throws caches nothing.
        /// </summary>
        public T GetOrAdd<T>(string key, Func<ICacheEntry, T> addItemFactory, MemoryCacheEntryOptions policy)
        {
            object CacheFactory(ICacheEntry entry) =>
                new Lazy<T>(() =>
                {
                    var result = addItemFactory(entry);
                    SetAbsoluteExpirationFromRelative(entry);
                    return result;
                });

            var cacheItem = (Lazy<T>)GetOrCreate(key, policy, CacheFactory);

            try
            {
                return cacheItem.Value;
            }
            catch //addItemFactory errored so do not cache the exception
            {
                cache.Remove(key);
                throw;
            }
        }

        /// <summary>
        ///     Gets the task under the key, or adds the task of the factory. A task that faults or is cancelled is not cached.
        /// </summary>
        public async Task<T> GetOrAddAsync<T>(string key, Func<ICacheEntry, Task<T>> addItemFactory,
            MemoryCacheEntryOptions policy)
        {
            object CacheFactory(ICacheEntry entry) =>
                new AsyncLazy<T>(async () =>
                {
                    var result = await addItemFactory(entry).ConfigureAwait(false);
                    SetAbsoluteExpirationFromRelative(entry);
                    return result;
                });

            var cacheItem = (AsyncLazy<T>)GetOrCreate(key, policy, CacheFactory);

            try
            {
                var result = cacheItem.Value;

                if (result.IsCanceled || result.IsFaulted)
                    cache.Remove(key);

                return await result.ConfigureAwait(false);
            }
            catch //addItemFactory errored so do not cache the exception
            {
                cache.Remove(key);
                throw;
            }
        }

        // Places the lazy into the cache under a per-key lock; the lazy's value is evaluated outside it.
        private object GetOrCreate(string key, MemoryCacheEntryOptions policy, Func<ICacheEntry, object> factory)
        {
            uint hash = (uint)key.GetHashCode() % (uint)keyLocks.Length;
            while (Interlocked.CompareExchange(ref keyLocks[hash], 1, 0) == 1) { Thread.Yield(); }

            try
            {
                if (!cache.TryGetValue(key, out var result))
                {
                    var entry = cache.CreateEntry(key);
                    // Set the initial options before the factory is fired so that any callbacks
                    // that need to be wired up are still added.
                    entry.SetOptions(policy);
                    result = factory(entry);
                    entry.SetValue(result);
                    // need to manually call dispose instead of having a using
                    // in case the factory passed in throws, in which case we
                    // do not want to add the entry to the cache
                    entry.Dispose();
                }

                return result;
            }
            finally
            {
                keyLocks[hash] = 0;
            }
        }

        // An expiry relative to now, set by the factory, counts from when the factory finishes.
        private static void SetAbsoluteExpirationFromRelative(ICacheEntry entry)
        {
            if (!entry.AbsoluteExpirationRelativeToNow.HasValue) return;

            var absoluteExpiration = DateTimeOffset.UtcNow + entry.AbsoluteExpirationRelativeToNow.Value;
            if (!entry.AbsoluteExpiration.HasValue || absoluteExpiration < entry.AbsoluteExpiration)
                entry.AbsoluteExpiration = absoluteExpiration;
        }
    }
}
