# Meshia Mesh Simplification


- [English](#english)
- [日本語](#日本語)

[Documents](https://ramtype0.github.io/Meshia.MeshSimplification/)

## English
Mesh simplification tool/library for Unity, VRChat.

Based on Unity Job System, and Burst. 
Provides fast, asynchronous mesh simplification.

Can be executed at runtime or in the editor.

### Installation

### VPM

Add [my VPM repository](https://ramtype0.github.io/VpmRepository/) to VCC, then add Meshia Mesh Simplification package to your projects.


### How to use

#### NDMF integration

Attach `MeshiaMeshSimplifier` to your models.

You can preview the result in EditMode.


#### Use from C#

```csharp

using Meshia.MeshSimplification;

Mesh simplifiedMesh = new();

// Asynchronous API

await MeshSimplifier.SimplifyAsync(originalMesh, target, options, simplifiedMesh);

// Synchronous API

MeshSimplifier.Simplify(originalMesh, target, options, simplifiedMesh);

```

## 日本語

Unity、VRChat向けのメッシュ軽量化ツールです。
Unity Job Systemで動作するため、Burstと合わせて高速、かつ非同期で処理ができるのが特徴です。
ランタイム、エディターの双方で動作します。

### インストール

### VPM

[VPM repository](https://ramtype0.github.io/VpmRepository/)をVCCに追加してから、Manage Project > Manage PackagesからMeshia Mesh Simplificationをプロジェクトに追加してください。

### 使い方

#### NDMF統合

NDMFがプロジェクトにインポートされている場合、`MeshiaMeshSimplifier`が使えます。
エディターで軽量化結果をプレビューしながらパラメーターの調整ができます。

#### C#から呼び出す

```csharp

using Meshia.MeshSimplification;

Mesh simplifiedMesh = new();

// 非同期API

await MeshSimplifier.SimplifyAsync(originalMesh, target, options, simplifiedMesh);

// 同期API

MeshSimplifier.Simplify(originalMesh, target, options, simplifiedMesh);

```


