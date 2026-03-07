# GameObjectPool
[![unity-meta-check](https://github.com/AndanteTribe/GameObjectPool/actions/workflows/unity-meta-check.yml/badge.svg)](https://github.com/AndanteTribe/GameObjectPool/actions/workflows/unity-meta-check.yml)
[![Releases](https://img.shields.io/github/release/AndanteTribe/GameObjectPool.svg)](https://github.com/AndanteTribe/GameObjectPool/releases)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/GameObjectPool.svg)](./LICENSE)
[![openupm](https://img.shields.io/npm/v/jp.andantetribe.gameobjectpool?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/jp.andantetribe.gameobjectpool/)

[English](README.md) | 日本語

## 概要
**GameObjectPool** は、Unity向けの非同期対応 GameObject プールライブラリです。

`MonoBehaviour` インスタンスを効率的に再利用し、繰り返しのインスタンス化・破棄によるオーバーヘッドを削減します。インスタンスはプールから借り出し、使用後に返却します。スコープAPI（`RentScopeAsync`）を使うことで、`using` 文による自動返却を簡単に実現できます。

## 要件
- Unity 6000.0 以上
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.10 以上
- [ObjectReference](https://github.com/AndanteTribe/ObjectReference) 0.1.2 以上

## インストール
`Window > Package Manager` からPackage Managerウィンドウを開き、`[+] > Add package from git URL` を選択して以下のURLを入力します。

```
https://github.com/AndanteTribe/GameObjectPool.git?path=src/GameObjectPool.Unity/Packages/jp.andantetribe.gameobjectpool
```

## クイックスタート

```csharp
using System;
using AndanteTribe.Unity.Extensions;
using Cysharp.Threading.Tasks;
using ObjectReference;
using UnityEngine;

public class PoolExample : MonoBehaviour
{
    [SerializeReference]
    IObjectReference<MyComponent> prefab;

    private GameObjectPool<MyComponent> _pool;

    private async UniTaskVoid Start()
    {
        _pool = new GameObjectPool<MyComponent>(transform, prefab, capacity: 4);

        // インスタンスを事前確保する
        await _pool.PreallocateAsync(4, destroyCancellationToken);

        // using スコープで借り出し（自動返却）。scope.Instance でインスタンスにアクセス
        using (var scope = await _pool.RentScopeAsync(destroyCancellationToken))
        {
            var instance = scope.Instance;
            instance.transform.position = Vector3.zero;
            await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: destroyCancellationToken);
        } // スコープ終了時に自動的にプールへ返却される
    }

    private void OnDestroy()
    {
        // プール内のすべてのインスタンスを破棄し、参照を解放する
        _pool?.Dispose();
    }
}
```

## API

| メソッド | 説明 |
|--------|------|
| `PreallocateAsync(int count, CancellationToken cancellationToken = default)` | 指定した数のインスタンスをプールに事前確保します。 |
| `RentAsync(CancellationToken cancellationToken = default)` | プールからインスタンスを借り出します。プールが空の場合は新しいインスタンスを生成します。 |
| `RentScopeAsync(CancellationToken cancellationToken = default)` | プールからインスタンスを借り出し、`using` 文で利用可能な `RentScope` を返します。スコープが破棄されると自動的に返却されます。 |
| `Return(T element)` | インスタンスをプールに返却します。 |
| `Clear()` | プール内のすべてのインスタンスを破棄し、プールをクリアします。 |
| `Dispose()` | `Clear()` と同等の処理に加え、オブジェクト参照を解放します。 |

## ライセンス
このライブラリは、MITライセンスで公開しています。
