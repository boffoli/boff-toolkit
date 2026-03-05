using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using BoffToolkit.Logging;

namespace BoffToolkit.Pooling {
    /// <summary>
    /// Manages a pool of objects for each key, reusing objects instead of creating new ones each time.
    /// This improves performance and reduces the overhead of object creation.
    /// </summary>
    /// <typeparam name="TKey">The type of the key used to identify each pool.</typeparam>
    /// <typeparam name="TValue">The type of objects stored in the pools, which must implement <see cref="IPoolable"/>.</typeparam>
    public class PoolManager<TKey, TValue> : IAsyncDisposable
        where TKey : notnull
        where TValue : class, IPoolable {
        private readonly ConcurrentDictionary<TKey, ConcurrentQueue<TValue>> _pool = new();
        private readonly Func<TKey, TValue> _instanceCreator;
        private readonly int? _maxInstancesPerKey;
        private readonly PoolCleaner<TKey, TValue> _poolCleaner;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="PoolManager{TKey, TValue}"/> class with the specified parameters.
        /// </summary>
        /// <param name="instanceCreator">A function that creates a new instance of the object.</param>
        /// <param name="maxInstancesPerKey">The maximum number of instances to maintain in the pool for each key.</param>
        /// <param name="cleanupInterval">The interval between periodic cleanups.</param>
        /// <param name="maxIdleTime">The maximum idle time before an object is removed.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="instanceCreator"/>, <paramref name="cleanupInterval"/>, or <paramref name="maxIdleTime"/> is null or zero.
        /// </exception>
        public PoolManager(Func<TKey, TValue> instanceCreator, int maxInstancesPerKey, TimeSpan cleanupInterval, TimeSpan maxIdleTime) {
            _instanceCreator = instanceCreator ?? throw new ArgumentNullException(nameof(instanceCreator));
            _maxInstancesPerKey = maxInstancesPerKey;

            // Create and start the pool cleaner
            _poolCleaner = new PoolCleaner<TKey, TValue>(_pool, cleanupInterval, maxIdleTime);
            _poolCleaner.StartCleaning();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PoolManager{TKey, TValue}"/> class using
        /// a factory that does not depend on the key.
        /// </summary>
        /// <param name="instanceCreator">A function that creates a new instance of the object.</param>
        /// <param name="maxInstancesPerKey">The maximum number of instances to maintain in the pool for each key.</param>
        /// <param name="cleanupInterval">The interval between periodic cleanups.</param>
        /// <param name="maxIdleTime">The maximum idle time before an object is removed.</param>
        public PoolManager(Func<TValue> instanceCreator, int maxInstancesPerKey, TimeSpan cleanupInterval, TimeSpan maxIdleTime)
            : this(_ => instanceCreator(), maxInstancesPerKey, cleanupInterval, maxIdleTime) {
        }

        /// <summary>
        /// Retrieves an instance of the object for the specified key, or creates a new one if none are available.
        /// </summary>
        /// <param name="key">The key for which to retrieve or create the object.</param>
        /// <param name="activationParams">Optional parameters used to configure the object during activation.</param>
        /// <returns>An instance of the object for the specified key, activated and ready for use.</returns>
        public async Task<TValue> GetOrCreateAsync(TKey key, params object[] activationParams) {
            if (!_pool.TryGetValue(key, out var instances) || instances.IsEmpty) {
                CentralLogger<PoolManager<TKey, TValue>>.LogInformation($"No instance available in the pool, creating a new instance for key {key}.");
                return await CreateNewInstanceAsync(key, activationParams);
            }

            while (instances.TryDequeue(out var instance)) {
                await instance.ActivateAsync(activationParams);
                CentralLogger<PoolManager<TKey, TValue>>.LogInformation($"Instance for key {key} has been activated.");

                if (await instance.ValidateAsync()) {
                    CentralLogger<PoolManager<TKey, TValue>>.LogInformation($"Instance already activated for key {key}, validated successfully.");
                    return instance;
                }
                else {
                    CentralLogger<PoolManager<TKey, TValue>>.LogInformation($"Validation failed for instance with key {key}, disposing the instance.");
                    await instance.DisposeAsync();
                }
            }

            CentralLogger<PoolManager<TKey, TValue>>.LogInformation($"Creating a new instance for key {key} after validation failures.");
            return await CreateNewInstanceAsync(key, activationParams);
        }

        /// <summary>
        /// Releases an instance of the object into the pool, preparing it for reuse.
        /// </summary>
        /// <param name="key">The key associated with the object instance.</param>
        /// <param name="instance">The object instance to release.</param>
        public async Task ReleaseAsync(TKey key, TValue instance) {
            await instance.DeactivateAsync();
            CentralLogger<PoolManager<TKey, TValue>>.LogInformation($"Instance deactivated for key {key}.");

            var instances = _pool.GetOrAdd(key, _ => new ConcurrentQueue<TValue>());
            if (instances.Count < _maxInstancesPerKey) {
                instances.Enqueue(instance);
                CentralLogger<PoolManager<TKey, TValue>>.LogInformation($"Instance returned to the pool for key {key}.");
            }
            else {
                CentralLogger<PoolManager<TKey, TValue>>.LogInformation($"The pool for key {key} is full, disposing the instance.");
                await instance.DisposeAsync();
            }
        }

        /// <summary>
        /// Creates a new instance of the object and activates it using the provided creation function.
        /// </summary>
        /// <param name="key">The key for which to create the object.</param>
        /// <param name="activationParams">Optional parameters used to configure the object during activation.</param>
        /// <returns>A new instance of the object, already activated.</returns>
        private async Task<TValue> CreateNewInstanceAsync(TKey key, params object[] activationParams) {
            var newInstance = _instanceCreator(key);
            await newInstance.ActivateAsync(activationParams);
            return newInstance;
        }

        /// <summary>
        /// Releases resources used by the pool manager.
        /// </summary>
        public async ValueTask DisposeAsync() {
            await DisposeAsyncCore();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes of resources in a manner specific to the derived type.
        /// </summary>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
        protected virtual ValueTask DisposeAsyncCore() {
            if (!_disposed) {
                _poolCleaner?.StopCleaning();
                _disposed = true;
            }
#if NET8_0
            return ValueTask.CompletedTask; // Returns ValueTask for .NET 8.0
#else
            return new ValueTask(Task.CompletedTask); // Converts Task to ValueTask for older frameworks
#endif
        }
    }
}
