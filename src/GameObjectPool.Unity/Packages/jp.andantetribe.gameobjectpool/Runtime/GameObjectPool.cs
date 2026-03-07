#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ObjectReference;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AndanteTribe.Unity.Extensions
{
    /// <summary>
    /// A pool for reusing Unity GameObjects.
    /// </summary>
    /// <example>
    /// <code>
    /// <![CDATA[
    /// using System;
    /// using AndanteTribe.Unity.Extensions;
    /// using Cysharp.Threading.Tasks;
    /// using UnityEngine;
    ///
    /// public class PoolExample : MonoBehaviour
    /// {
    ///     [SerializeReference]
    ///     IObjectReference<MyComponent> prefab;
    ///
    ///     private async UniTaskVoid Start()
    ///     {
    ///         var pool = new GameObjectPool<MyComponent>(transform, prefab, capacity: 4);
    ///
    ///         // Preallocate
    ///         await pool.PreallocateAsync(4);
    ///
    ///         // Rent with using scope for automatic return (access instance via scope.Instance)
    ///         using (var scope = await pool.RentScopeAsync())
    ///         {
    ///             var instance = scope.Instance;
    ///             instance.transform.position = Vector3.zero;
    ///             await UniTask.Delay(TimeSpan.FromSeconds(1));
    ///         } // Automatically returned when scope exits
    ///     }
    /// }
    /// ]]>
    /// </code>
    /// </example>
    /// <typeparam name="T"></typeparam>
    public sealed class GameObjectPool<T> : IDisposable where T : MonoBehaviour
    {
        /// <summary>
        /// The root object of the pool.
        /// </summary>
        private readonly Transform _root;

        /// <summary>
        /// Reference to the pool's original object.
        /// </summary>
        private readonly IObjectReference<T> _reference;

        /// <summary>
        /// The pool stack.
        /// </summary>
        private readonly List<T> _pool;


        public int Count => _pool.Count;

        public GameObjectPool(Transform root, IObjectReference<T> reference, int capacity)
        {
            _root = root;
            _reference = reference;
            _pool = new List<T>(capacity);
        }

        /// <summary>
        /// Preallocates instances to the pool.
        /// </summary>
        /// <param name="count"></param>
        /// <param name="cancellationToken"></param>
        public async UniTask PreallocateAsync(int count, CancellationToken cancellationToken = default)
        {
            var original = await _reference.LoadAsync(cancellationToken);
            var instances = await Object.InstantiateAsync(original, count, _root).WithCancellation(cancellationToken);
            foreach (var instance in instances)
            {
                instance.gameObject.SetActive(false);
                _pool.Add(instance);
            }
            _pool.TrimExcess();
        }

        /// <summary>
        /// Rents an object from the pool.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async UniTask<T> RentAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_pool.Count > 0)
            {
                var last = _pool.Count - 1;
                var instance = _pool[last];
                _pool.RemoveAt(last);
                instance.gameObject.SetActive(true);
                return instance;
            }

            var original = await _reference.LoadAsync(cancellationToken);
            var results = await Object.InstantiateAsync(original, _root).WithCancellation(cancellationToken);
            return results[0];
        }

        /// <summary>
        /// Asynchronously rents an instance from the pool (method for use with using statement).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The rented instance and <see cref="System.IDisposable"/> for release.</returns>
        public async UniTask<RentScope> RentScopeAsync(CancellationToken cancellationToken = default)
        {
            var instance = await RentAsync(cancellationToken);
            return new RentScope(this, instance);
        }

        /// <summary>
        /// Returns an object to the pool.
        /// </summary>
        /// <param name="element">The instance to return.</param>
        public void Return(T element)
        {
            if (element != null)
            {
                element.gameObject.SetActive(false);
                element.transform.SetParent(_root);
                _pool.Add(element);
            }
        }

        /// <summary>
        /// Clears the pool.
        /// </summary>
        public void Clear()
        {
            foreach (var item in _pool)
            {
                Object.Destroy(item.gameObject);
            }
            _pool.Clear();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Clear();
            _reference.Dispose();
        }

        /// <summary>
        /// Scope struct for instances rented from the pool.
        /// </summary>
        /// <remarks>
        /// Typically used within a using scope.
        /// </remarks>
        public readonly struct RentScope : IDisposable
        {
            private readonly GameObjectPool<T> _gameObjectPool;

            /// <summary>
            /// The instance rented from the pool.
            /// </summary>
            public readonly T Instance;

            internal RentScope(GameObjectPool<T> gameObjectPool, T instance)
            {
                _gameObjectPool = gameObjectPool;
                Instance = instance;
            }

            void IDisposable.Dispose() => _gameObjectPool.Return(Instance);
        }
    }
}
