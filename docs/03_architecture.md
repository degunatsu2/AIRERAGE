# 03. システム構成・フォルダ構成・命名規則

> ステータス: ドラフト v0.1（2026-10-12）。推奨案です。承認後に確定します。

---

## 1. 基本方針

| # | 方針 | 理由 |
|---|---|---|
| 1 | **シミュレーション（ゲームルールの真実）とプレゼンテーション（見た目・音・UI）を分ける** | 判定・硬直・ゲージ・ネットワーク上の結果と、アニメーションの状態を混同しないため |
| 2 | **シミュレーションは固定ティック【仮】60Hz で進める**。`Time.deltaTime` を使わない | 操作感とフレームデータを安定させ、ネットワークの予測や再計算を可能にするため |
| 3 | **シミュレーションのコアは UnityEngine に依存しない純C#にする** | Unity 無しで自動テストできる（このクラウド環境でも `dotnet test` で実行できることを確認済み）。予測やロールバックに必要な決定性を確保しやすい |
| 4 | **入力はティック単位のコマンド構造体にする** | ネットワークでは入力だけを送る。入力を記録して再生すれば、不具合を再現できる |
| 5 | **状態は明示的な構造体で持つ** | ネットワーク同期、巻き戻し、デバッグ表示、テストでの検証が容易になる |
| 6 | **調整用の数値はデータ（ScriptableObject）に置く** | コードを編集せずに調整するため。コード中に数値を散在させない |
| 7 | **ネットワーク層はシミュレーションの外側に置く** | ローカルとオンラインで同じシミュレーションを使う。ネットワークライブラリへの依存を1つのアセンブリに閉じ込める |
| 8 | **重要な結果は、確定ティックのシミュレーション結果を正とする**（ロールバック方式。D-14）。結果を決める処理はシミュレーションの中に一本化し、ネットワークでは入力だけを送る | 命中、コイン、ゲージ、撃破、勝敗の二重加算や不正な確定を防ぐ |
| 9 | **シングルトンやグローバル状態を増やさない** | 構成ルート（Bootstrap）で依存を組み立て、明示的に渡す |

---

## 2. 全体構成

```
┌────────────────────────── Unity 側（Degunatsu.Runtime）──────────────────────────┐
│ Unity Input System → InputAdapter ──┐                                            │
│                                      ▼                                            │
│                              PlayerInput（ティックごと）                            │
│                                      │        ┌── Degunatsu.Net（Phase 2〜）────┐ │
│                                      │◀──────▶│ 入力の送受信（全員の入力）        │ │
│                                      ▼        │ 予測・巻き戻し（ロールバック）    │ │
│  SimulationRunner（ティックを回す） ──▶ ┌──────────────────────────────────┐       │ │
│                                        │ Degunatsu.Core（純C#・固定ティック） │       │ │
│                                        │  Physics ：BoxTerrain（地形）        │       │ │
│  SceneTerrainBuilder ────────────────▶ │  Movement：KinematicMotor            │       │ │
│  （シーンの BoxCollider を読む）       │  Wire    ：WireController            │       │ │
│                                        │            ＋ IWireMotionModel(A|B)  │       │ │
│                                        │  Combat  ：ActionStateMachine 等     │       │ │
│                                        │  Economy ：CoinLedger                │       │ │
│                                        │  Match   ：MatchRules                │       │ │
│                                        └──────────────────────────────────┘       │ │
│                                      │ 状態（読み取り専用）                        └──┘ │
│                                      ▼                                            │
│  Presentation：CharacterView / WireView / カメラ / HUD / エフェクト / Animator      │
└──────────────────────────────────────────────────────────────────────────────────┘
```

### 2.1 アセンブリ（asmdef）と依存方向

```
Degunatsu.Core        （noEngineReferences: true。UnityEngine を参照しない）
   ▲
Degunatsu.Runtime     （Core を参照。MonoBehaviour、ScriptableObject、アダプター）
   ▲
Degunatsu.Net         （Core と Runtime を参照。ネットワークのライブラリを参照するのはここだけ。Phase 2〜）

Degunatsu.Editor      （Runtime を参照。エディタ拡張のみ）
Degunatsu.Core.Tests  （Core を参照。EditMode テスト。dotnet test でも実行する）
Degunatsu.Runtime.Tests（Runtime と Core.Tests を参照。EditMode テスト。シーンの読み込みなど）
Degunatsu.PlayMode.Tests（Runtime を参照。PlayMode テスト。シーンを再生する通しの確認。最小限）
```

- Core は Runtime・Net・UnityEngine を**絶対に参照しない**。asmdef の設定で強制する。
- 逆方向の参照が必要になったら、Core 側にインターフェースを置き、Runtime 側で実装する（例: `ICollisionQuery`、`ILog`）。

---

## 3. モジュールの責務

| モジュール | 責務 | 主な型（予定） | フェーズ |
|---|---|---|---|
| Core/Common | ティック、数学の補助、決定的な乱数、ログのインターフェース | `Tick`, `DeterministicRandom`, `ILog` | 1 |
| Core/Input | 入力コマンド、先行入力のバッファ | `PlayerInput`, `InputBuffer` | 1 |
| Core/Physics | 衝突クエリのインターフェースと、軸平行の箱でできた地形 | `ICollisionQuery`, `CastHit`, `BoxTerrain` | 1 |
| Core/Movement | 走行、ジャンプ、重力、地形との衝突解決（すり抜け防止の連続判定） | `KinematicMotor`, `MovementConfig` | 1 |
| Core/Wire | ワイヤーのフェーズ管理、照準、接続判定、解除、強制解除、クールダウン、ロープの巻き付き（D-33） | `WireController`, `WireState`, `WireTargeting`, `RopeWrapping`, `RopePivots` | 1 |
| Core/Wire/Models | 接続中の運動計算（差し替え可能） | `IWireMotionModel`, `InertialWireModel`, `ResponsiveWireModel` | 1 |
| Core/Simulation | ワールドの状態、ティックの進行、キャラクター状態、状態のバイト列とチェックサム（PC 間で同じ値）、ステージの範囲 | `SimWorld`, `CharacterState`, `StateCodec`, `StageBounds` | 1 |
| Core/Replay | 入力の記録と再生（開始時の状態＋入力列＋最後の状態のチェックサム。テキストで保存） | `InputRecorder`, `InputRecording` | 1 |
| Core/Rollback | ロールバックの層（入力の管理、入力遅延、予測、巻き戻しと再計算、確定ティック）と入力の送受信の規則（冗長送信、確認応答、メッセージのバイト列）。通信はしない。docs/10 | `RollbackSession`, `RollbackSettings`, `InputExchange`, `InputMessage`, `InputNormalizer`, `TimeSync`（状態の保存は Simulation の `WorldSnapshot`） | 2 |
| Core/Combat | アクション状態（Free / Attack など）と遷移、攻撃のデータ（フレームデータ）、キャンセル規則、命中判定、ガード。docs/11 | `ActionController`, `ActionState`, `CombatState`, `CombatTuning`, `AttackData`（判定 `AttackHit` と動き `AttackMotion` の並び）, `HitResolver`, `KnockoutResolver`（ステージの外に出たら撃破） | 3 |
| Core/Economy | コイン台帳（イベントIDで冪等、総量保存） | `CoinLedger`, `CoinTransaction` | 4 |
| Core/Match | 試合進行、タイマー、出現、復帰、勝敗 | `MatchRules`, `MatchState` | 4 |
| Runtime/Simulation | ティックループ、シーンの地形の読み込み | `SimulationRunner`, `SceneTerrainBuilder` | 1 |
| Runtime/Input | Input System から `PlayerInput` を作る | `InputSystemAdapter` | 1 |
| Runtime/Data | ScriptableObject の定義と、Core の設定への変換 | `WireProfileAsset` など | 1 |
| Runtime/Presentation | 状態を読んで表示する。カメラのズーム（表示だけ） | `CharacterView`, `WireView`, `AimIndicatorView`, `CameraZoom`, `ChargeEffectView`, `RingGaugeView`, `GuardEffectView`, `BurstEffectView`, `ComboHud` | 1 |
| Runtime/Audio | 仮の BGM と効果音（音声ファイルを使わず、その場で合成する。状態の変化から鳴らす。差し替え可能） | `GameAudio`, `ProceduralAudio`, `CombatSoundEvents` | 3 |
| Runtime/DebugTools | デバッグHUD、軌跡表示、スロー再生、タイプ切り替え、計測（速さ、コースのタイム）、入力の記録と再生 | `DebugHud`, `DebugControls`, `TrailView`, `MotionStats`, `CourseTimer`, `CourseTimerHud`, `CourseZone`, `WireTypeLabels`, `ReplayControls` | 1 |
| Net | 通信路（Unity Transport。検証用の遅延の模擬つき）、セッション（参加、開始、入力の中継、不一致の検出）、巻き戻しの表示の補正、オンラインの検証用の画面。ロールバックの本体は Core/Rollback | `TransportLink`, `NetMessages`, `OnlineSession`, `CorrectionSmoother`, `OnlineRunner` | 2 |

---

## 4. 主要な設計

### 4.1 シミュレーションと固定ティック

- `SimulationRunner`（MonoBehaviour）が `Update` で経過時間を蓄積し、1/60秒ごとに `SimWorld.Step(inputs)` を呼ぶ。Unity の `FixedUpdate` には依存しない（ネットワークの再計算で同じ処理を何度も呼べるようにするため）。
- 描画は直前と現在のティックの状態を補間して滑らかにする。
- Core の中では `Time`、`UnityEngine.Random`、`System.DateTime`、静的な可変状態を使わない。乱数は試合ごとのシードから作る `DeterministicRandom` を使う。

### 4.2 キャラクターの移動は自前のキネマティック制御

- **Rigidbody は使わない**。速度と位置を自前で積分し、地形との衝突は `ICollisionQuery`（箱のスイープ、重なりの解消、レイキャスト）で解決する。キャラクターの当たり形状は軸平行の箱。
- 理由:
  - ネットワークの予測では、自キャラだけを数ティック分再計算する必要がある。Unity の物理エンジンはシーン全体でしか進められない。
  - 操作感（加速、空中制御、ワイヤーの拘束）を直接制御できる。
  - 決定性を保ちやすい。
- 地形は静的。動く足場などのギミックは「ティックから位置が一意に決まる」形で作る（予測と矛盾させないため）。
- **地形の衝突判定は Core の `BoxTerrain`（純C#）で行い、Unity の物理クエリ（PhysX）は使わない**（D-32）。
  - シーンには地形を `BoxCollider` で置き、開始時に `SceneTerrainBuilder` が `BoxTerrain` に読み込む。条件は「地形の層にある」「回転していない」「Z = 0 の面を横切る」。条件に合わないコライダーはエラーを出して除く。
  - 理由: PhysX の `BoxCast` は形状を約2mm膨らませて扱い（距離が短くなる、表面から2mm以内で始めると当たらない）、テストの結果と一致しなかった（2026-10-12 の計測）。純C#なら決定的で、テスト（`dotnet test`）と同じ結果になる。
  - 制約: 地形は軸平行の箱だけ（斜面や回転した形は未対応。必要になったら `BoxTerrain` を拡張する）。
- アリーナには「細長い壁」があるため、高速移動時のすり抜けを**連続判定（スイープ）**で防ぐ。これを自動テストで保証する。
- ステージの範囲（外に出ると撃破。docs/11 T3-3）は Core の `StageBounds`（長方形）で、`SimWorld` に渡す。シーンでは `KnockoutBounds` を1つ置く（なければ撃破はない）。

### 4.3 ワイヤー: 共通の制御＋差し替え可能な運動モデル

```csharp
// 接続中の1ティック分の運動を計算する。フェーズ遷移（解除など）は WireController が決める。
public interface IWireMotionModel
{
    WireMotionResult Step(in WireMotionContext context);
}
```

- `WireController`（共通）: フェーズ管理、照準とエイムアシスト、接続判定、最大長、強制解除、クールダウン、速度の上限、解除時の慣性、キャンセル判定のためのフェーズ公開。
- `IWireMotionModel`（差し替え）: 接続中の速度とロープ長の変化だけを計算する。
  - `InertialWireModel`（タイプA）: ロープ長の拘束、重力、巻き取り、スティックによる振り。遅い状態で接続した直後の強い引き（接続した瞬間の速さは `WireState.AttachSpeed`）。
  - `ResponsiveWireModel`（タイプB）: アンカーへ向かう目標速度への収束、スティックによる進路の調整。
  - 接続中のスティックの入力による速さの倍率（`WireTuning.AttachedInputInfluence`）は、`WireMotionContext.InputSpeedScale` で共通に計算する（進む方向だけをモデルが渡す。今はタイプB が使う）。
- どちらを使うかは `WireProfile` データの `modelType` で選ぶ。
- **共通の契約テスト**（速度上限を超えない、壁をすり抜けない、解除時の速度が規則どおり、など）を、NUnit のパラメータ化テストで両モデルに同じように適用する。

### 4.4 データ（ScriptableObject）と Core の設定

- Core には `[Serializable]` な純C#の設定クラス（例: `WireTuning`）を置く。
- Runtime の ScriptableObject（例: `WireProfileAsset`）はそれを1つのフィールドとして持ち、インスペクターで編集できるようにする。
- 実行時に Core の設定へ変換し、`Validate()` で値の範囲を検査する。不正な値はアセット名とフィールド名を含むエラーで止める（握りつぶさない）。
- 実装済みの設定: `MovementTuning`, `WireTuning`（`InertialWireTuning` / `ResponsiveWireTuning` を含む）、それらをまとめた `CharacterConfig`。
- 予定しているデータ: `CharacterDefinition`, `AttackDefinition`, `GuardTuning`, `GaugeTuning`, `EconomyRules`, `MatchRules`, `DepthPolicy`。
- 開発中はプレイ中に ScriptableObject を編集すると即座に反映されるようにする（調整の往復を速くするため）。

### 4.5 入力

- `InputSystemAdapter` がティックごとに Input System を読み取り、`PlayerInput` を作る。
- `PlayerInput` の内容: 移動の方向、照準の方向（マウスでの照準と、ゲームパッドの右スティックでの照準のとき。なければ移動の方向で照準する）、ボタンのビットマスク。方向は量子化した整数。ネットワークで送りやすく、端末による誤差が出ないようにするため量子化する。
- 「押した瞬間」の判定と先行入力（【仮】6ティック）は Core 側で行う。

### 4.6 アニメーションとゲームロジック

- `CharacterView` が `CharacterState`（アクション状態と、その状態に入ってからの経過ティック）を読み、Animator のパラメータやクリップの再生位置を決める。
- アニメーションイベントで攻撃判定を出したり、状態を遷移させたりしない。

### 4.7 エラー処理とログ

- 例外を空の catch で握りつぶさない。
- Core では状態遷移の失敗理由を返せるようにする（例: キャンセルできなかった理由）。デバッグHUDに表示する。
- ログは `ILog` を経由する（Runtime で `Debug.Log` に接続する）。
- 入力の記録と再生の仕組みを用意し、不具合を確実に再現できるようにする（Phase 1 の後半）。

### 4.8 パッケージ（導入は承認後）

| パッケージ | 用途 | 導入時期 |
|---|---|---|
| Input System | 入力 | Phase 0 |
| Test Framework | EditMode / PlayMode テスト | Phase 0（Unity に同梱） |
| URP | 描画（テンプレートに含まれる） | Phase 0 |
| Cinemachine 3.x | カメラの追従、デッドゾーン、ダンピング | Phase 1（カメラはシミュレーションに影響しないので後から差し替え可能）。**導入済み**: 3.1.7（2026-10-12、ユーザーの承認。依存で Splines 2.9.1 も入った） |
| Unity Transport 2.x | 通信路（UDP）。ロールバックの層は自前（D-14、D-15） | Phase 2。**導入済み**: 2.7.4（2026-10-13、ユーザーの承認。当初の候補だった Netcode for GameObjects は使わない） |
| Multiplayer Play Mode 2.0 | エディタ上で最大4プレイヤーを起動して検証 | Phase 2。**導入済み**: 2.0.2（2026-10-13、ユーザーの承認） |
| Multiplayer Tools | ネットワークシミュレーター（遅延・パケットロスの模擬）、プロファイラ | Phase 2 |
| Multiplayer Services | セッション（参加コード）、Relay（NAT越え） | Phase 6 |

各パッケージは導入する時点で、Unity のバージョンとの互換性と最新版を確認する。

---

## 5. フォルダ構成

Unity プロジェクトをリポジトリ直下に置く前提です（D-04）。

```
degunatsu/
├─ CLAUDE.md                     # Claude Code 向けのプロジェクト規約
├─ README.md
├─ docs/                         # 設計書（本書を含む）
├─ tools/
│  └─ CoreTests/                 # dotnet test 用のプロジェクト（Core と EditMode テストのソースを参照）
├─ Assets/
│  └─ _Project/                  # 自作アセットはすべてここ（サードパーティと分ける）
│     ├─ Scripts/
│     │  ├─ Core/                # Degunatsu.Core.asmdef（UnityEngine 非依存）
│     │  │  ├─ Common/
│     │  │  ├─ Input/
│     │  │  ├─ Physics/
│     │  │  ├─ Movement/
│     │  │  ├─ Wire/
│     │  │  │  └─ Models/
│     │  │  ├─ Simulation/
│     │  │  ├─ Replay/
│     │  │  ├─ Rollback/         # Phase 2
│     │  │  ├─ Combat/           # Phase 3
│     │  │  ├─ Economy/          # Phase 4
│     │  │  └─ Match/            # Phase 4
│     │  ├─ Runtime/             # Degunatsu.Runtime.asmdef
│     │  │  ├─ Bootstrap/
│     │  │  ├─ Simulation/
│     │  │  ├─ Input/
│     │  │  ├─ Data/
│     │  │  ├─ Presentation/
│     │  │  ├─ Camera/
│     │  │  └─ DebugTools/
│     │  ├─ Net/                 # Degunatsu.Net.asmdef（Phase 2）
│     │  └─ Editor/              # Degunatsu.Editor.asmdef
│     ├─ Tests/
│     │  ├─ EditMode/            # Degunatsu.Core.Tests.asmdef（純C#）
│     │  ├─ Runtime/             # Degunatsu.Runtime.Tests.asmdef（EditMode）
│     │  └─ PlayMode/            # Degunatsu.PlayMode.Tests.asmdef
│     ├─ Data/                   # ScriptableObject アセット
│     │  ├─ Characters/
│     │  ├─ Movement/
│     │  ├─ Wire/
│     │  ├─ Attacks/
│     │  └─ Rules/
│     ├─ Input/                  # .inputactions
│     ├─ Prefabs/
│     ├─ Scenes/                 # Sandbox_Wire.unity、Sandbox_Online.unity、Sandbox_Combat.unity（どれも Editor のビルダーで生成する）
│     ├─ Art/
│     ├─ Materials/
│     ├─ Audio/
│     └─ Settings/               # URP の設定アセットなど
├─ Packages/
└─ ProjectSettings/
```

### 5.1 Unity 外でのテスト（tools/CoreTests）

- `tools/CoreTests/CoreTests.csproj` が `Assets/_Project/Scripts/Core/**/*.cs` と `Assets/_Project/Tests/EditMode/**/*.cs` をソースとして取り込み、NUnit で実行する。
- 同じテストを Unity の Test Runner（EditMode）でも実行できる。テストは UnityEngine を使わない。
- `LangVersion` を Unity 6 の C# バージョン（C# 9）に合わせ、Unity でコンパイルできない新しい構文を検出する。
- Core はベクトル型に `System.Numerics.Vector2/Vector3` を使う（Unity と .NET の両方で使える）。Unity の型とは Runtime の境界で変換する。
- このクラウド環境では、.NET 8 SDK を apt で導入し、NUnit のテストが動くことを確認済み。セッションのたびに自動で導入する仕組み（SessionStart フック）は Phase 0 で用意する。

---

## 6. 命名規則

| 対象 | 規則 | 例 |
|---|---|---|
| 名前空間 | `Degunatsu.<アセンブリ>.<機能>` | `Degunatsu.Core.Wire` |
| 型・メソッド・プロパティ | PascalCase | `WireController`, `TryFire()` |
| private フィールド | `_camelCase`（インスペクターでは先頭の `_` が除かれて表示される） | `[SerializeField] private float _maxSpeed;` |
| Core の設定クラスのフィールド | PascalCase の public フィールド（Core は UnityEngine の `SerializeField` を使えないため。Unity は public フィールドをシリアライズする） | `public float RunSpeed = 8f;` |
| ローカル変数・引数 | camelCase | `ropeLength` |
| インターフェース | `I` で始める | `IWireMotionModel` |
| 単位を含む名前 | 時間はティック数なら `Ticks`、秒なら `Seconds`。角度は `Deg` / `Rad` | `cooldownTicks`, `aimAssistAngleDeg` |
| 単位 | 距離は m（Unity の 1単位 = 1m）、速度は m/s、加速度は m/s² | — |
| ScriptableObject のクラス | `〜Asset` または `〜Definition` | `WireProfileAsset`, `AttackDefinition` |
| ScriptableObject のアセット | `種類_名前_バリエーション` | `WireProfile_Inertial_Default` |
| シーン | 検証用は `Sandbox_`、本番のアリーナは `Arena_` | `Sandbox_Wire`, `Arena_Factory` |
| テスト | `対象_条件_期待結果` | `Fire_NoTargetInRange_EntersRetracting` |
