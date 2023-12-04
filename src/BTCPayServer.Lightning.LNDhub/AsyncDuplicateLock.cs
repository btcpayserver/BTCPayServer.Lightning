#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Lightning.LndHub;

///from https://stackoverflow.com/a/31194647/275504
public sealed class AsyncDuplicateLock
{
    private sealed class RefCounted<T>
    {
        public RefCounted(T value)
        {
            RefCount = 1;
            Value = value;
        }

        public int RefCount { get; set; }
        public T Value { get; private set; }
    }

    private readonly ConcurrentDictionary<object, RefCounted<SemaphoreSlim>?> _semaphoreSlims = new();

    private SemaphoreSlim GetOrCreate(object key)
    {
        RefCounted<SemaphoreSlim>? item;
        lock (_semaphoreSlims)
        {
            if (_semaphoreSlims.TryGetValue(key, out item) && item is { })
            {
                ++item.RefCount;
            }
            else
            {
                item = new RefCounted<SemaphoreSlim>(new SemaphoreSlim(1, 1));
                _semaphoreSlims[key] = item;
            }
        }
        return item.Value;
    }
    
    // get a lock for a specific key, and wait until it is available
    public async Task<IDisposable> LockAsync(object key, CancellationToken cancellationToken = default)
    {
        await GetOrCreate(key).WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(_semaphoreSlims, key);
    }
    
    // get a lock for a specific key if it is available, or return null if it is currently locked 
    public async Task<IDisposable?> LockOrBustAsync(object key, CancellationToken cancellationToken = default)
    {
        var semaphore = GetOrCreate(key);
        if (semaphore.CurrentCount == 0)
            return null;
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(_semaphoreSlims, key);
    }
    private sealed class Releaser : IDisposable
    {
        private readonly ConcurrentDictionary<object, RefCounted<SemaphoreSlim>?> _semaphoreSlims;

        public Releaser(ConcurrentDictionary<object, RefCounted<SemaphoreSlim>?> semaphoreSlims, object key)
        {
            _semaphoreSlims = semaphoreSlims;
            Key = key;
        }

        private object Key { get; set; }

        public void Dispose()
        {
            RefCounted<SemaphoreSlim>? item;
            lock (_semaphoreSlims)
            {
                if (_semaphoreSlims.TryGetValue(Key, out item) && item is { })
                {
                    --item.RefCount;
                    if (item.RefCount == 0)
                        _semaphoreSlims.TryRemove(Key, out _);
                }
            }
            item?.Value.Release();
        }
    }
}
