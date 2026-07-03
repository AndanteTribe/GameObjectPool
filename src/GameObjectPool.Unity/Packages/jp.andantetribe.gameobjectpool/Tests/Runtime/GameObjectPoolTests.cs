#nullable enable

using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ObjectReference;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AndanteTribe.Unity.Extensions.Tests
{
    /// <summary>
    /// Play mode tests for GameObjectPool.
    /// </summary>
    public sealed class GameObjectPoolTests
    {
        private Transform? _root;
        private PooledComponent? _prefab;
        private TestObjectReference<PooledComponent>? _reference;
        private GameObjectPool<PooledComponent>? _pool;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("PoolRoot").transform;
            _prefab = new GameObject("PooledPrefab").AddComponent<PooledComponent>();
            _reference = new TestObjectReference<PooledComponent>(_prefab);
            _pool = new GameObjectPool<PooledComponent>(_root, _reference, capacity: 2);
        }

        [TearDown]
        public void TearDown()
        {
            _pool?.Dispose();
            CleanupGameObject(_root?.gameObject);
            CleanupGameObject(_prefab?.gameObject);
        }

        [Test]
        public void Count_InitiallyZero()
        {
            Assert.That(_pool!.Count, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator PreallocateAsync_CreatesInactiveInstancesUnderRoot() => UniTask.ToCoroutine(async () =>
        {
            await _pool!.PreallocateAsync(3);

            Assert.That(_pool.Count, Is.EqualTo(3));
            Assert.That(_reference!.LoadCount, Is.EqualTo(1));
            Assert.That(_root!.childCount, Is.EqualTo(3));

            for (var i = 0; i < _root.childCount; i++)
            {
                Assert.That(_root.GetChild(i).gameObject.activeSelf, Is.False);
            }
        });

        [UnityTest]
        public IEnumerator RentAsync_WithPreallocatedInstance_ReusesInstanceAndActivatesIt() => UniTask.ToCoroutine(async () =>
        {
            await _pool!.PreallocateAsync(1);
            var pooledInstance = _root!.GetChild(0).GetComponent<PooledComponent>();

            var rentedInstance = await _pool.RentAsync();

            Assert.That(rentedInstance, Is.SameAs(pooledInstance));
            Assert.That(rentedInstance.gameObject.activeSelf, Is.True);
            Assert.That(_pool.Count, Is.EqualTo(0));
            Assert.That(_reference!.LoadCount, Is.EqualTo(1));

            _pool.Return(rentedInstance);
        });

        [UnityTest]
        public IEnumerator RentAsync_WhenPoolIsEmpty_InstantiatesPrefabUnderRoot() => UniTask.ToCoroutine(async () =>
        {
            var rentedInstance = await _pool!.RentAsync();

            Assert.That(rentedInstance, Is.Not.SameAs(_prefab));
            Assert.That(rentedInstance.name, Does.StartWith(_prefab!.name));
            Assert.That(rentedInstance.transform.parent, Is.EqualTo(_root));
            Assert.That(rentedInstance.gameObject.activeSelf, Is.True);
            Assert.That(_pool.Count, Is.EqualTo(0));
            Assert.That(_reference!.LoadCount, Is.EqualTo(1));

            _pool.Return(rentedInstance);
        });

        [UnityTest]
        public IEnumerator Return_DeactivatesInstanceAndParentsItToRoot() => UniTask.ToCoroutine(async () =>
        {
            var rentedInstance = await _pool!.RentAsync();
            var otherParent = new GameObject("OtherParent");

            try
            {
                rentedInstance.transform.SetParent(otherParent.transform);

                _pool.Return(rentedInstance);

                Assert.That(rentedInstance.gameObject.activeSelf, Is.False);
                Assert.That(rentedInstance.transform.parent, Is.EqualTo(_root));
                Assert.That(_pool.Count, Is.EqualTo(1));
            }
            finally
            {
                CleanupGameObject(otherParent);
            }
        });

        [UnityTest]
        public IEnumerator RentScopeAsync_DisposeReturnsInstanceToPool() => UniTask.ToCoroutine(async () =>
        {
            PooledComponent rentedInstance;

            using (var scope = await _pool!.RentScopeAsync())
            {
                rentedInstance = scope.Instance;
                Assert.That(rentedInstance.gameObject.activeSelf, Is.True);
                Assert.That(_pool.Count, Is.EqualTo(0));
            }

            Assert.That(rentedInstance.gameObject.activeSelf, Is.False);
            Assert.That(rentedInstance.transform.parent, Is.EqualTo(_root));
            Assert.That(_pool.Count, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Clear_RemovesAllPooledInstances() => UniTask.ToCoroutine(async () =>
        {
            await _pool!.PreallocateAsync(2);

            _pool.Clear();

            Assert.That(_pool.Count, Is.EqualTo(0));
            await UniTask.NextFrame();
            Assert.That(_root!.childCount, Is.EqualTo(0));
        });

        [UnityTest]
        public IEnumerator Dispose_ClearsPoolAndDisposesReference() => UniTask.ToCoroutine(async () =>
        {
            await _pool!.PreallocateAsync(2);

            _pool.Dispose();

            Assert.That(_pool.Count, Is.EqualTo(0));
            Assert.That(_reference!.IsDisposed, Is.True);
            await UniTask.NextFrame();
            Assert.That(_root!.childCount, Is.EqualTo(0));

            _pool = null;
        });

        [UnityTest]
        public IEnumerator RentAsync_WithCancellation_ThrowsOperationCanceledException() => UniTask.ToCoroutine(async () =>
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var exceptionThrown = false;
            try
            {
                await _pool!.RentAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                exceptionThrown = true;
            }

            Assert.That(exceptionThrown, Is.True);
            Assert.That(_pool!.Count, Is.EqualTo(0));
            Assert.That(_reference!.LoadCount, Is.EqualTo(0));
        });

        private static void CleanupGameObject(GameObject? obj)
        {
            if (obj != null)
            {
                Object.DestroyImmediate(obj);
            }
        }

        private sealed class PooledComponent : MonoBehaviour
        {
        }

        private sealed class TestObjectReference<T> : IObjectReference<T> where T : Object
        {
            private readonly T _value;

            public TestObjectReference(T value)
            {
                _value = value;
            }

            public int LoadCount { get; private set; }

            public bool IsDisposed { get; private set; }

            public ValueTask<T> LoadAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LoadCount++;
                return new ValueTask<T>(_value);
            }

            public ValueTask<T> LoadAsync(IProgress<float> progress, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LoadCount++;
                progress.Report(1.0f);
                return new ValueTask<T>(_value);
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }
    }
}