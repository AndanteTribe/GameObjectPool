# GameObjectPool
[![unity-meta-check](https://github.com/AndanteTribe/GameObjectPool/actions/workflows/unity-meta-check.yml/badge.svg)](https://github.com/AndanteTribe/GameObjectPool/actions/workflows/unity-meta-check.yml)
[![Releases](https://img.shields.io/github/release/AndanteTribe/GameObjectPool.svg)](https://github.com/AndanteTribe/GameObjectPool/releases)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/GameObjectPool.svg)](./LICENSE)

English | [日本語](README_JA.md)

## Overview
**GameObjectPool** is an async-friendly GameObject pool for Unity.

It allows you to efficiently reuse `MonoBehaviour` instances, reducing the overhead of repeated instantiation and destruction. Instances are rented from the pool and returned after use. A scoped API (`RentScopeAsync`) makes it easy to ensure automatic returns via `using` statements.

## Requirements
- Unity 6000.0 or later
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.10 or later
- [ObjectReference](https://github.com/AndanteTribe/ObjectReference) 0.1.2 or later

## Installation
Open `Window > Package Manager`, select `[+] > Add package from git URL`, and enter the following URL:

```
https://github.com/AndanteTribe/GameObjectPool.git?path=src/GameObjectPool.Unity/Packages/jp.andantetribe.gameobjectpool
```

## Quick Start

```csharp
using System;
using AndanteTribe.Unity.Extensions;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class PoolExample : MonoBehaviour
{
    [SerializeReference]
    IObjectReference<MyComponent> prefab;

    private GameObjectPool<MyComponent> _pool;

    private async UniTaskVoid Start()
    {
        _pool = new GameObjectPool<MyComponent>(transform, prefab, capacity: 4);

        // Preallocate instances
        await _pool.PreallocateAsync(4, destroyCancellationToken);

        // Rent with a using scope for automatic return (access the instance via scope.Instance)
        using (var scope = await _pool.RentScopeAsync(destroyCancellationToken))
        {
            var instance = scope.Instance;
            instance.transform.position = Vector3.zero;
            await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: destroyCancellationToken);
        } // Automatically returned to the pool when the scope exits
    }

    private void OnDestroy()
    {
        // Destroys all pooled instances and disposes the reference
        _pool?.Dispose();
    }
}
```

## API

| Method | Description |
|--------|-------------|
| `PreallocateAsync(int count, CancellationToken cancellationToken = default)` | Preallocates the specified number of instances into the pool. |
| `RentAsync(CancellationToken cancellationToken = default)` | Rents an instance from the pool. If the pool is empty, a new instance is instantiated. |
| `RentScopeAsync(CancellationToken cancellationToken = default)` | Rents an instance from the pool and returns a `RentScope` for use with a `using` statement. The instance is automatically returned when the scope is disposed. |
| `Return(T element)` | Returns an instance to the pool. |
| `Clear()` | Destroys all pooled instances and clears the pool. |
| `Dispose()` | Equivalent to `Clear()`, and additionally disposes the object reference. |

## License
This library is released under the MIT license.
