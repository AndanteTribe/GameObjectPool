#nullable enable

using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
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
        public IEnumerator PreallocateAsync_CreatesInactiveInstancesUnderRoot() => Run(async () =>
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
        public IEnumerator RentAsync_WithPreallocatedInstance_ReusesInstanceAndActivatesIt() => Run(async () =>
        {
            await _pool!.PreallocateAsync(1);
            var pooledInstance = _root!.GetChild(0).GetComponent<PooledComponent>();

            var rent = _pool.RentAsync();
            Assert.That(rent.IsCompletedSuccessfully, Is.True);
            var rentedInstance = await rent;

            Assert.That(rentedInstance, Is.SameAs(pooledInstance));
            Assert.That(rentedInstance.gameObject.activeSelf, Is.True);
            Assert.That(_pool.Count, Is.EqualTo(0));
            Assert.That(_reference!.LoadCount, Is.EqualTo(1));

            _pool.Return(rentedInstance);
        });

        [UnityTest]
        public IEnumerator RentAsync_WhenPoolIsEmpty_InstantiatesPrefabUnderRoot() => Run(async () =>
        {
            _root!.position = new Vector3(5, 6, 7);
            _root.rotation = Quaternion.Euler(0, 45, 0);
            _prefab!.transform.position = new Vector3(1, 2, 3);
            _prefab.transform.rotation = Quaternion.Euler(10, 20, 30);
            var rentedInstance = await _pool!.RentAsync();

            Assert.That(rentedInstance, Is.Not.SameAs(_prefab));
            Assert.That(rentedInstance.name, Does.StartWith(_prefab!.name));
            Assert.That(rentedInstance.transform.parent, Is.EqualTo(_root));
            Assert.That(Vector3.Distance(rentedInstance.transform.position, _prefab.transform.position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(rentedInstance.transform.rotation, _prefab.transform.rotation), Is.LessThan(0.01f));
            Assert.That(rentedInstance.gameObject.activeSelf, Is.True);
            Assert.That(_pool.Count, Is.EqualTo(0));
            Assert.That(_reference!.LoadCount, Is.EqualTo(1));

            _pool.Return(rentedInstance);
        });

        [UnityTest]
        public IEnumerator Return_DeactivatesInstanceAndParentsItToRoot() => Run(async () =>
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
        public IEnumerator RentScopeAsync_DisposeReturnsInstanceToPool() => Run(async () =>
        {
            PooledComponent rentedInstance;

            var pending = _pool!.RentScopeAsync();
            Assert.That(pending.IsCompletedSuccessfully, Is.True);
            using (var scope = await pending)
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
        public IEnumerator Clear_RemovesAllPooledInstances() => Run(async () =>
        {
            await _pool!.PreallocateAsync(2);

            _pool.Clear();

            Assert.That(_pool.Count, Is.EqualTo(0));
            await Awaitable.NextFrameAsync();
            Assert.That(_root!.childCount, Is.EqualTo(0));
        });

        [UnityTest]
        public IEnumerator Dispose_ClearsPoolAndDisposesReference() => Run(async () =>
        {
            await _pool!.PreallocateAsync(2);

            _pool.Dispose();

            Assert.That(_pool.Count, Is.EqualTo(0));
            Assert.That(_reference!.IsDisposed, Is.True);
            await Awaitable.NextFrameAsync();
            Assert.That(_root!.childCount, Is.EqualTo(0));

            _pool = null;
        });

        [UnityTest]
        public IEnumerator RentAsync_WithCancellation_ThrowsOperationCanceledException() => Run(async () =>
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

        private static readonly string[] ApiCases = { "Preallocate", "Rent", "RentScope" };

        [UnityTest]
        public IEnumerator DelayedLoad_CompletesSuccessfully(
            [ValueSource(nameof(ApiCases))] string api) => Run(async () =>
        {
            var completion = new TaskCompletionSource<PooledComponent>();
            _reference!.OnLoad = _ => new ValueTask<PooledComponent>(completion.Task);
            var pending = Call(api);
            Assert.That(pending.IsCompleted, Is.False);
            await Awaitable.NextFrameAsync();
            completion.SetResult(_prefab!);
            await pending;
            var expected = api == "Preallocate" ? 10 : 1;
            Assert.That(_pool!.Count, Is.EqualTo(expected));
            Assert.That(_root!.childCount, Is.EqualTo(expected));
            Assert.That(_reference.LoadCount, Is.EqualTo(1));
            for (var i = 0; i < expected; i++)
                Assert.That(_root.GetChild(i).gameObject.activeSelf, Is.False);
        });

        [UnityTest]
        public IEnumerator AlreadyCanceled_DoesNotLoadOrInstantiate(
            [ValueSource(nameof(ApiCases))] string api) => Run(async () =>
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await AssertCanceled(Call(api, cts.Token), cts.Token);
            Assert.That(_reference!.LoadCount, Is.Zero);
            await AssertNoClones();
        });

        [UnityTest]
        public IEnumerator CancelDuringLoad_PropagatesLoaderCancellation(
            [ValueSource(nameof(ApiCases))] string api) => Run(async () =>
        {
            using var cts = new CancellationTokenSource();
            var completion = new TaskCompletionSource<PooledComponent>();
            using var registration = cts.Token.Register(() => completion.TrySetCanceled(cts.Token));
            _reference!.OnLoad = _ => new ValueTask<PooledComponent>(completion.Task);
            var pending = Call(api, cts.Token);
            Assert.That(pending.IsCompleted, Is.False);
            cts.Cancel();
            await AssertCanceled(pending, cts.Token);
            Assert.That(_reference.LoadCount, Is.EqualTo(1));
            await AssertNoClones();
        });

        [UnityTest]
        public IEnumerator CancelImmediatelyAfterStarting_DoesNotLeak(
            [ValueSource(nameof(ApiCases))] string api) => Run(async () =>
        {
            using var cts = new CancellationTokenSource();
            var pending = Call(api, cts.Token);
            Assert.That(pending.IsCompleted, Is.False, "Must cancel an in-flight operation.");
            cts.Cancel();
            await AssertCanceled(pending, cts.Token);
            await AssertNoClones();
        });

        [UnityTest]
        public IEnumerator CanceledCachedRent_DoesNotRemoveOrActivateInstance() => Run(async () =>
        {
            await _pool!.PreallocateAsync(1);
            var instance = _root!.GetChild(0).gameObject;
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await AssertCanceled(Call("Rent", cts.Token), cts.Token);
            await AssertCanceled(Call("RentScope", cts.Token), cts.Token);
            Assert.That(_pool.Count, Is.EqualTo(1));
            Assert.That(instance.activeSelf, Is.False);
            Assert.That(_reference!.LoadCount, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator LoadFailure_PropagatesOriginalExceptionWithoutCreatingObjects(
            [ValueSource(nameof(ApiCases))] string api) => Run(async () =>
        {
            var expected = new InvalidOperationException("Load failed");
            _reference!.OnLoad = _ => new ValueTask<PooledComponent>(Task.FromException<PooledComponent>(expected));
            Exception? actual = null;
            try { await Call(api); }
            catch (Exception exception) { actual = exception; }
            Assert.That(actual, Is.SameAs(expected));
            await AssertNoClones();
        });

        [UnityTest]
        public IEnumerator ConcurrentCalls_CancelingOneDoesNotDestroyTheOther() => Run(async () =>
        {
            using var cts = new CancellationTokenSource();
            var canceled = _pool!.PreallocateAsync(10, cts.Token);
            var successful = _pool.RentAsync();
            cts.Cancel();
            await AssertCanceled(canceled, cts.Token);
            var rented = await successful;
            await Awaitable.NextFrameAsync();
            await Awaitable.NextFrameAsync();
            Assert.That(rented != null, Is.True);
            Assert.That(_root!.childCount, Is.EqualTo(1));
            Assert.That(_pool.Count, Is.Zero);
            _pool.Return(rented!);
        });

        [UnityTest]
        public IEnumerator CancelAfterSuccess_DoesNotDestroyOwnedInstances() => Run(async () =>
        {
            using var cts = new CancellationTokenSource();
            await _pool!.PreallocateAsync(2, cts.Token);
            var rented = await _pool.RentAsync(cts.Token);
            cts.Cancel();
            await Awaitable.NextFrameAsync();
            Assert.That(rented != null, Is.True);
            Assert.That(_pool.Count, Is.EqualTo(1));
            Assert.That(_root!.childCount, Is.EqualTo(2));
            _pool.Return(rented!);
        });

        [UnityTest]
        public IEnumerator FailedCancellation_DoesNotPreventSubsequentUse() => Run(async () =>
        {
            using var cts = new CancellationTokenSource();
            var pending = _pool!.PreallocateAsync(10, cts.Token);
            Assert.That(pending.IsCompleted, Is.False);
            cts.Cancel();
            await AssertCanceled(pending, cts.Token);
            await AssertNoClones();
            await _pool.PreallocateAsync(2);
            var rented = await _pool.RentAsync();
            Assert.That(rented != null, Is.True);
            _pool.Return(rented!);
            Assert.That(_pool.Count, Is.EqualTo(2));
            Assert.That(_root!.childCount, Is.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator Preallocate_InvalidCount_DoesNotLoad() => Run(async () =>
        {
            foreach (var count in new[] { 0, -1 })
            {
                Exception? actual = null;
                try { await _pool!.PreallocateAsync(count); }
                catch (Exception exception) { actual = exception; }
                Assert.That(actual, Is.TypeOf<ArgumentOutOfRangeException>());
            }
            Assert.That(_reference!.LoadCount, Is.Zero);
            await AssertNoClones();
        });

        // A native API regression check: pin the exact 0.9 integration boundary rather than
        // relying on a fast prefab happening to spend a frame at that progress value.
        [UnityTest]
        public IEnumerator NativeCancellation_At90_CompletesWithoutNullDereference() => Run(async () =>
        {
            using var cts = new CancellationTokenSource();
            var operation = Object.InstantiateAsync(_prefab!, 10,
                new InstantiateParameters { parent = _root, worldSpace = true }, cts.Token);
            operation.allowSceneActivation = false;
            try
            {
                while (operation.progress < 0.9f)
                    await Awaitable.NextFrameAsync();
                Assert.That(operation.isDone, Is.False);
                var waiting = AwaitNative(operation);
                cts.Cancel();
                // The native awaiter reports cancellation; its exception need not carry our token.
                var canceled = false;
                try { await waiting; }
                catch (OperationCanceledException) { canceled = true; }
                Assert.That(canceled, Is.True);
                Assert.That(operation.isDone, Is.True);
                await AssertNoClones();
            }
            finally
            {
                operation.allowSceneActivation = true;
                if (!operation.isDone) operation.Cancel();
            }
        });

        private static async ValueTask AwaitNative(AsyncInstantiateOperation<PooledComponent> operation)
        {
            await operation;
        }

        private async ValueTask Call(string api, CancellationToken token = default)
        {
            switch (api)
            {
                case "Preallocate": await _pool!.PreallocateAsync(10, token); break;
                case "Rent": _pool!.Return(await _pool.RentAsync(token)); break;
                case "RentScope":
                    using (await _pool!.RentScopeAsync(token)) { }
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(api));
            }
        }

        private static async ValueTask AssertCanceled(ValueTask pending, CancellationToken token)
        {
            OperationCanceledException? actual = null;
            try { await pending; }
            catch (OperationCanceledException exception) { actual = exception; }
            Assert.That(actual, Is.Not.Null, "Expected cancellation, not success or a null result.");
            // Preserve the native exception; Unity's awaiter need not include this token.
            Assert.That(token.IsCancellationRequested, Is.True);
        }

        private async ValueTask AssertNoClones()
        {
            // Wait beyond deferred Destroy and check again for late native completion.
            for (var i = 0; i < 5; i++) await Awaitable.NextFrameAsync();
            Assert.That(_pool!.Count, Is.Zero);
            Assert.That(_root!.childCount, Is.Zero);
            var components = Object.FindObjectsByType<PooledComponent>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(components, Has.Length.EqualTo(1));
            Assert.That(components[0], Is.SameAs(_prefab));
        }

        private static IEnumerator Run(Func<Task> test)
        {
            var task = test();
            var deadline = Time.realtimeSinceStartup + 15;
            while (!task.IsCompleted)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Async test timed out.");
                yield return null;
            }
            task.GetAwaiter().GetResult();
        }

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

            public Func<CancellationToken, ValueTask<T>>? OnLoad { get; set; }

            public ValueTask<T> LoadAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LoadCount++;
                return OnLoad != null ? OnLoad(cancellationToken) : new ValueTask<T>(_value);
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