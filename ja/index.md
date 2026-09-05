# ShapeSync チュートリアル & ドキュメント

ようこそ！本ドキュメントは、Unity 向けキャラクター衣装追従ツールセット **ShapeSync（シェイプシンク）** の公式チュートリアルです。

ShapeSync を使うことで、ベースとなるキャラクター（Figure）の体型変化に合わせて、衣装（Outfit）を登録・追従させることができます。

---

## 📦 チュートリアル補助素材

第3章の初期動作確認に使用するアニメーション素材です。

- [CC0Animation.unitypackage](../CC0Animation.unitypackage)（CC0 1.0）

---

## 📖 チュートリアル章構成（目次）

本チュートリアルは以下の全13章で構成されています。初めて導入される方は、第1章から順にお進みください。

| # | 章 | 主な作業 | 状態 |
| :--- | :--- | :--- | :--- |
| 1 | **[第1章: インストール](./installation.md)** | package 導入。前提（Unity version、Graphics API、URP、Asset Serialization Mode）をここでまとめて提示する | 公開中 |
| 2 | **[第2章: 初期 VRM データ生成](./initialvrm.md)** | Base Figure と FBM の VRM を作る | 公開中 |
| 3 | **[第3章: Figure 登録](./figureregistration.md)** | Database 上で Figure を登録し、生成、Scene 配置、初期動作確認を行う | 公開中 |
| 4 | **[第4章: Outfit 登録](./outfitregistration.md)** | 着衣状態の VRM を作成し、Outfit を登録して動作確認を行う | 公開中 |
| 5 | **[第5章: Shape 登録](./shaperegistration.md)** | Morph / Skin / Hair / Outfit Shape を登録して動作確認を行う | 公開中 |
| 6 | **[第6章: 高度な Figure 登録 / PBM](./pbmregistration.md)** | PBM 用 VRM 作成と Database 登録、Morph Shape の再定義と動作確認 | 公開中 |
| 7 | **[第7章: 高度な Outfit 登録 / Collection](./outfitcollection.md)** | 靴着用 VRM の作成と Database 登録、Projection と Collection 設定、動作確認 | 公開中 |
| 8 | **[第8章: 高度な Outfit 登録 / Mask](./figuremask.md)** | VRoid Studio 内での靴補正用 Mask 作成、Database への Mask 登録、動作確認 | 公開中 |
| 9 | **[第9章: VRM 連携](./vrmintegration.md)** | 表情と Physics 用 VRM を登録し、生成と動作確認を行う | 公開中 |
| 10 | **[第10章: Document 保存](./documentstorage.md)** | ここまでの Shape をまとめ、Document を保存する | 公開中 |
| 11 | **[第11章: Humanoid Compiler](./humanoidcompiler.md)** | 作成した Document から Humanoid を生成する | 公開中 |
| 12 | **[第12章: Atlas](./atlas.md)** | Atlas Editor による設定と Humanoid 再生成 | 公開中 |
| 13 | **[第13章: Hot Bake](./hotbake.md)** | Figure / Document / Atlas による Runtime Humanoid 再生 | 公開中 |

---

## 🔗 各章の詳細とリンク

### [第1章: インストール](./installation.md)（公開中）
* ShapeSync の導入に必要な動作環境（Unity 6 / URP / DX12 等）
* OpenUPM レジストリおよび NuGetForUnity の設定
* 必須パッケージ（R3、ShapeSync Core）のインストール手順
* プロジェクトの初期設定（Color Space: Linear、Asset Serialization Mode: Mixed）
* オプション: VRM 連携パッケージ（UniVRM / Companion）の導入
* **[よくあるトラブルと解決策（トラブルシューティング）](./installation.md#6-よくあるトラブルと解決策トラブルシューティング)**

### [第2章: 初期 VRM データ生成](./initialvrm.md)（公開中）
* VRoid Studio を使用した Base VRM（`BasicFemale`）および FBM VRM（`SampleI`）の準備
* 同一トポロジ維持のための髪型・衣装・アクセサリーの全除去
* VRM 1.0 エクスポートとポリゴン・マテリアル・ボーン数の一致確認
* VRoid Studio サンプルモデルの利用規約について
* **[よくあるトラブルと解決策（トラブルシューティング）](./initialvrm.md#6-よくあるトラブルと解決策トラブルシューティング)**

### [第3章: Figure 登録](./figureregistration.md)（公開中）
* ShapeSync Editor の起動と Database（`.prefab`）の作成
* Figure セクションでの Base VRM（`BasicFemale.vrm`）登録
* Materials セクションでの 9 件の Material Entry 命名
* FBMs セクションでの FBM 軸（`SampleI` / `SampleI.vrm`）登録（`Import All Materials and Textures` 有効化）
* Generation セクションでの Figure 生成（Generate）
* Scene 配置と CC0 Animation（`Walking.controller`）再生下での FBM weight 動作確認
* **[よくあるトラブルと解決策（トラブルシューティング）](./figureregistration.md#8-よくあるトラブルと解決策トラブルシューティング)**

### [第4章: Outfit 登録](./outfitregistration.md)（公開中）
* VRoid Studio を使用したカスタムヘア（`Hair1`）およびプリセットドレス（`Dress1`）の Base / FBM 用 VRM 準備
* ShapeSync Editor の Outfits セクション（Mesh Outfits）での `Hair1` / `Dress1` 登録
* Mesh Outfit Materials でのマテリアル分類（`Include` / `Exclude`）
* FBMs セクションでの `SampleI` 軸用 VRM 割り当て
* Generation セクションでの Outfit Prefab 出力（Generate）
* Scene 配置した Figure への `OutfitAttacher` 設定と Play Mode での着脱・体型変形追従確認
* **[よくあるトラブルと解決策（トラブルシューティング）](./outfitregistration.md#7-よくあるトラブルと解決策トラブルシューティング)**

### [第5章: Shape 登録](./shaperegistration.md)（公開中）
* VRoid Studio からのストッキングテクスチャ書き出し（`Stocking.png`）と Unity での `Alpha is Transparency` 設定
* Outfits セクションでの Material Outfit（`Stocking`）登録（テクスチャ名 `Body` 割り当て）
* Shapes セクションでの各種 Shape 登録（Morph: `morphSampleI`、Hair: `hairSampleI`、Skin: `skinSampleI`、Outfit: `outfitSampleI`）
* Generation セクションでの Shape Template アセット群およびカタログの出力（Generate）
* Figure の `ShapeDirector` への Template 登録と Play Mode（`Walking.controller`）での連動制御・動作確認
* **[よくあるトラブルと解決策（トラブルシューティング）](./shaperegistration.md#10-よくあるトラブルと解決策トラブルシューティング)**

### [第6章: 高度な Figure 登録 / PBM](./pbmregistration.md)（公開中）
* VRoid Studio を使用した局所体型変形（`BreastSize`）の Base / FBM 用 `.vroid` データ作成
* 素体用（`BreastSizeBasicFemale.vrm`, `BreastSizeSampleI.vrm`）および衣装用（`Dress1BreastSizeBasicFemale.vrm`, `Dress1BreastSizeSampleI.vrm`）の PBM VRM エクスポート
* Figure セクションでの PBM（`BreastSize`）登録と Base / FBM VRM 割り当て
* Outfits セクションでの Dress への PBM 追従設定（`Follow BreastSize`）
* Shapes セクションでの Morph Shape（`morphSampleI`）上書き再定義（`SampleI = 1`, `BreastSize = 0.8`）
* Generation セクションでの再 Generate 実行
* Figure の `ShapeDirector` による Play Mode での PBM 胸部連動変形の動作確認
* **[よくあるトラブルと解決策（トラブルシューティング）](./pbmregistration.md#10-よくあるトラブルと解決策トラブルシューティング)**

### [第7章: 高度な Outfit 登録 / Collection](./outfitcollection.md)（公開中）
* VRoid Studio を使用した靴のカスタムアイテム化と素体（Base / FBM）への着用・VRM エクスポート
* Scene 上での素体と靴の位置・姿勢ずれの確認と Collection 機能の概要
* Outfits セクションでの靴（`Shoes1`）登録および身体マテリアルの `Projection` 分類
* Figure からの素体 Prefab Export（`Collection/Shoes1`）と Scene 上での姿勢（Hip Y / Foot X 回転）調整・Override 保存
* Collections セクションでの `Full` および `Use Projection for Full Collection` 設定
* Outfit Shape（`outfitShoes1`）登録、Generate、および `ShapeDirector` による靴 Fit 動作確認と残存 Poke の確認
* **[よくあるトラブルと解決策（トラブルシューティング）](./outfitcollection.md#9-よくあるトラブルと解決策トラブルシューティング)**

### [第8章: 高度な Outfit 登録 / Mask](./figuremask.md)（公開中）
* 第7章で残存した靴のつま先・足裏の突き抜け（Poke）の確認と Figure Mask の仕組み
* VRoid Studio の 3D Paint によるつま先・足裏用マスクテクスチャ（`Shoes1Mask.png`）の作成と保存（`Assets/Texture/`）
* マスク極性（黒 = 非表示 / 隠す、白 = 表示 / 残す）と塗り広げない目視基準
* Outfits セクションでの Figure Mask 登録（`Figure Material Entry: Body`、`Mask Texture: Shoes1Mask.png`）
* Textures セクションへの個別登録が不要な仕様の解説
* Generation セクションでの再 Generate と Play Mode での Poke 解消確認（Before / After）
* **[よくあるトラブルと解決策（トラブルシューティング）](./figuremask.md#5-よくあるトラブルと解決策トラブルシューティング)**

### [第9章: VRM 連携](./vrmintegration.md)（公開中）
* VRM 連携に必要な前提環境（UniVRM パッケージおよび `SHAPESYNC_USE_UNIVRM`）とスキップの判断
* Figure セクションでの Expression Reference（Base: `BasicFemale.vrm`, FBM: `SampleI.vrm`）および Physics Reference（`Hair1BasicFemale.vrm`）登録
* Figure 側 Physics Reference の任意性（揺れ物を持つ VRM であれば任意）の解説
* Outfits セクションでの各衣装（`Hair1` / `Dress1`）への Physics Reference VRM 設定
* Generation セクションでの Generate 実行と自動後処理（Expression Bake / Physics 転送）の解説
* Play Mode での `UniversalExpressionProxy` による表情変化確認、および歩行再生下での揺れ物（SpringBone）追従動作確認（個別分離検証）
* **[よくあるトラブルと解決策（トラブルシューティング）](./vrmintegration.md#7-よくあるトラブルと解決策トラブルシューティング)**

### [第10章: Document 保存](./documentstorage.md)（公開中）
* VRoid Studio によるセパレート衣装（`Tops1` 4種、`Skirt1` 2種）の VRM 出力と登録
* Outfit Tag（`upperchest`, `lowerchest`）の作成と語彙登録
* Outfit Shape での Priority / Tag 設定（`outfitTops1`, `outfitSkirt1`, `outfitSampleI` 更新）
* Priority と Tag による排他制御（3 Template 登録時の Dress1 単独表示、Dress Remove 時のセパレート衣装表示）
* ShapeDirector による Document A（`ShapeSyncDocumentA`）および Document B（`ShapeSyncDocumentB`）の保存（`Assets/ShapeSync`）
* Template List 全解除からの Document A / B の Load 読み込みと状態復元確認
* **[よくあるトラブルと解決策（トラブルシューティング）](./documentstorage.md#7-よくあるトラブルと解決策トラブルシューティング)**

### [第11章: Humanoid Compiler](./humanoidcompiler.md)（公開中）
* Document A（`ShapeSyncDocumentA`）を入力とした Pure Humanoid 生成の概要
* Humanoid Compiler ウィンドウの起動と入力設定（Figure: `BasicFemale.prefab`、Document: `ShapeSyncDocumentA.asset`、Atlas Schema: 空欄）
* VRM 連携環境における `Transport VRM Physics` トグル設定
* 出力先フォルダー（`Assets/ShapeSync/Compiler/DocumentA/`）の指定と Generate 実行
* `DocumentA` 接頭語の生成物（`DocumentA.prefab`、`DocumentA.asset`、`DocumentA_avatar.asset`、Material / Texture 等）の確認
* Scene 配置と Pure Humanoid 構造（Unity 標準 Animator / Avatar を持ち ShapeSync 実行時コンポーネントを含まない構成）の確認
* **[よくあるトラブルと解決策（トラブルシューティング）](./humanoidcompiler.md#6-よくあるトラブルと解決策トラブルシューティング)**

### [第12章: Atlas](./atlas.md)（公開中）
* Atlas の目的（テクスチャ枚数・VRAM 使用量の削減と Draw Call 低減）と仕様
* Atlas Editor の起動と Figure（`BasicFemale.prefab`）/ Document（`ShapeSyncDocumentA.asset`）指定
* `List Entries` によるスナップショット取得と Entry 一覧・元テクスチャサイズ確認
* Page Size `2048`、Page 0（素体パーツ群）/ Page 1（衣装パーツ群）の専有面積（Occupancy）割当
* 髪型（`Hair1_*`）の `ignore` 設定（UV 交錯エラー防止）
* `Dry Run` による配置検証と Atlas Schema（`Assets/ShapeSync/AtlasSchema.asset`）の保存
* Humanoid Compiler での Atlas Schema 適用と新規フォルダー（`Assets/ShapeSync/Compiler/AtlasA/`）への再生成
* `AtlasA` 接頭語の生成アセット確認と Main Texture 1枚集約効果の解説
* Scene 配置と Pure Humanoid 構造・歩行アニメーション動作確認
* **[よくあるトラブルと解決策（トラブルシューティング）](./atlas.md#7-よくあるトラブルと警告エラー例トラブルシューティング)**

### [第13章: Hot Bake](./hotbake.md)（公開中）
* Hot Bake の仕組み（Editor 事前ビルドと Runtime 動的組み立ての違い）
* Empty GameObject への `Animator` および `HotBake Figure` コンポーネント追加
* 1体目（`HotBakeFigureA`）の設定（Figure: `BasicFemale.prefab`、Document: `ShapeSyncDocumentA.asset`、Atlas: `AtlasSchema.asset`、`Require Atlas: ON`、`Physics Transport: ON`）
* `Walking.controller` 設定と Play Mode での 1体目単独歩行確認
* 2体目（`HotBakeFigureB`）の複製・配置（Position: `(1,0,0)`）と設定（Document: `ShapeSyncDocumentB.asset`、Atlas: `None`、`Require Atlas: OFF`、`Physics Transport: ON`）
* 服装の異なる 2体の Humanoid の同時動的生成と並行歩行（2体同時 Walking）動作確認
* **[よくあるトラブルと解決策（トラブルシューティング）](./hotbake.md#43-うまく生成されない場合の確認トラブルシューティング)**

---

## 💡 はじめに知っておきたい基本用語

* **Figure**: 素体となる3Dキャラクターモデルです。
* **Outfit**: キャラクターが着用する衣装や装飾品です。
* **ShapeSync Core**: 衣装追従の基本機能を提供するコアパッケージです。
* **VRM Integration Companion**: VRM 1.0 形式のアバターモデルで ShapeSync を利用するための拡張パッケージです（オプション）。

---

*© 2026 zgock999. Released under the MIT License.*
