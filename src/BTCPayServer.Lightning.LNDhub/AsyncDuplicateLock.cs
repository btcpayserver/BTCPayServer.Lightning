using System;
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

    private readonly Dictionary<object, RefCounted<SemaphoreSlim>> _semaphoreSlims = new();

    private SemaphoreSlim GetOrCreate(object key)
    {
        RefCounted<SemaphoreSlim> item;
        lock (_semaphoreSlims)
        {
            if (_semaphoreSlims.TryGetValue(key, out item))
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
    public async Task<IDisposable> LockAsync(object key, CancellationToken cancellationToken = default)
    {
        await GetOrCreate(key).WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(_semaphoreSlims, key);
    }
    
    public async Task<IDisposable?> LockOrBustAsync(object key, CancellationToken cancellationToken = default)
    {
        var semaphore = GetOrCreate(key);
        if (semaphore.CurrentCount == 0)
            return null;
        await GetOrCreate(key).WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(_semaphoreSlims, key);
    }
    private sealed class Releaser : IDisposable
    {
        private readonly Dictionary<object, RefCounted<SemaphoreSlim>> _semaphoreSlims;

        public Releaser(Dictionary<object, RefCounted<SemaphoreSlim>> semaphoreSlims, object key)
        {
            _semaphoreSlims = semaphoreSlims;
            Key = key;
        }
        public object Key { get; set; }

        public void Dispose()
        {
            RefCounted<SemaphoreSlim> item;
            lock (_semaphoreSlims)
            {
                item = _semaphoreSlims[Key];
                --item.RefCount;
                if (item.RefCount == 0)
                    _semaphoreSlims.Remove(Key);
            }
            item.Value.Release();
        }
    }
}
