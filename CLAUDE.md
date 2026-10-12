# CLAUDE.md — degunatsu

Unity / C# で制作する、2.5D横視点のワイヤーアクション型オンラインPvP（4人、プレイヤーホスト型）。
Claude Code を使い、小さな単位で段階的に開発する。

## 現在の状態

- **Phase 1 の途中**。UnityEngine に依存しない Core（移動、ワイヤーの共通制御、タイプA/B）と自動テストは完成（`docs/06_prototype_plan.md` 5章の進捗表）。
- **Unity プロジェクトを作成済み**（2026-10-12、Unity 6000.3.26f1、「Universal 3D」テンプレート。D-04）。クラウドでも Unity をバッチモードで使える（`docs/08` 4.2）。Phase 0 は完了。T1-7（Unity との接続）と T1-8（検証用アリーナ。3コースとタイム計測）はユーザーの確認が済み、フィードバック（マウス照準、照準のインジケーター、解除後のクールダウン 0）を反映した。次は T1-9（デバッグと調整のための機能）。
- 決定済みの判断（D-01 Unity 6.3 LTS、D-03 PC/Windows、D-04、D-05 Git LFS、D-18、P-1〜P-4）は `docs/02_open_decisions.md` に記録してある。

## 設計書（作業前に関係する章を読む）

| ファイル | 内容 |
|---|---|
| `docs/01_game_design.md` | ゲームの基本仕様（コンセプト、コイン、空間、ワイヤー、戦闘、キャラクター） |
| `docs/02_open_decisions.md` | 未確定の仕様、判断事項（D-xx）、発見した問題点（P-x）。**決定はここに記録する** |
| `docs/03_architecture.md` | システム構成、フォルダ構成、命名規則 |
| `docs/04_network.md` | ネットワーク方式の候補、推奨構成、技術リスク |
| `docs/05_roadmap.md` | 開発フェーズと完了条件 |
| `docs/06_prototype_plan.md` | 最初のプロトタイプ（ワイヤー移動）の実装計画 |
| `docs/07_quality.md` | テストと品質管理の方針、手動プレイテストのチェックリスト |
| `docs/08_cloud_unity_setup.md` | クラウド環境で Unity Editor を動かすための設定（ユーザー側の設定が必要） |
| `docs/09_unity_project_setup.md` | Unity プロジェクトの作成手順（ユーザーのPCで行う場合） |

## 確定している基本原則（ユーザーの指示）

- 4つの面白さ（コイン争奪、空中コンボ、キャラクター対戦、目標争奪）を、**1つのアリーナ・1つの戦闘システム・1つのスコア設計**に統合する。別モードにしない。
- ワイヤーの2タイプ（A: 物理・慣性重視、B: 操作追従重視）は、ルール・戦闘・スコア・ネットワークを共有する。差は設定値か差し替え可能な運動ロジックだけで表し、**タイプごとにコードを複製しない**。
- キャンセル規則: 攻撃→攻撃は最大1回。攻撃→ワイヤーは可（クールタイムあり。使うとエフェクトを出し、キャラ周囲に半透明の円形クールタイマーを表示する。P-1）。ワイヤー→攻撃は接続中（`WireState.IsPulling`）の間だけ可（D-18）。状態遷移とキャンセルの可否は明示的に管理する。
- フレームデータ、ガード／ジャストガードの受付時間などはデータで調整できるようにする。未確定の数値は【仮】と明記し、最終仕様として固定しない。
- コインの獲得・所持・ドロップ・消滅・最終スコアへの反映は別の概念として扱う。報酬の発生と加算は一元管理し、二重加算や不正な加算を防ぐ。
- キャラクターは共通の戦闘基盤＋データ（ScriptableObject）＋最小限の固有ロジックで差別化する。
- 重要な結果（命中、ダメージ、ゲージ、コイン、撃破、勝敗）はホストが確定する。クライアントに自由に確定させない。オンラインの検証はローカル版の完成を待たずに早めに行う。

## コードの規約

- 巨大な MonoBehaviour に処理を集めない。状態管理と入力受付を分ける。アニメーションの状態とゲームロジックの状態を混同しない。
- 調整用の数値をコードに散在させない。グローバルな状態やシングルトンを安易に増やさない。
- 既存コードを確認せずに、新しい仕組みを重複して実装しない。不要な抽象化や複雑なパターンを入れない。
- エラーを握りつぶさない（空の catch を書かない）。原因を追える情報を残す。
- 未確定の機能を先回りして作り込まない。

### アーキテクチャ（`docs/03_architecture.md`。この方針で実装中）

- シミュレーションは固定ティック（60Hz、`SimTime`）の純C#（`Degunatsu.Core`、`noEngineReferences: true`）。`Time`、`UnityEngine.Random`、静的な可変状態を使わない。
- キャラクターの移動は Rigidbody を使わない自前のキネマティック制御（`KinematicMotor`）。当たり形状は軸平行の箱。衝突は `ICollisionQuery` を通して調べる。
- 地形は Core の `BoxTerrain`（純C#）。シーンでは回転しない `BoxCollider` で置き、`SceneTerrainBuilder` が読み込む。Unity の物理クエリ（PhysX）は使わない（D-32）。
- 入力はティック単位の `PlayerInput`（量子化。移動・照準の方向とボタン。キーボード＋マウスはマウスポインターの方向で照準する。D-08）。状態は構造体（`CharacterState` / `WireState`）で持ち、等値比較できるようにする。
- ワイヤーは共通の `WireController` と、差し替え可能な `IWireMotionModel`（`InertialWireModel` / `ResponsiveWireModel`）。モデルは速度とロープ長だけを決める。
- 調整値は Core の `[Serializable]` な設定クラス（`MovementTuning` / `WireTuning` / `CharacterConfig`）に置き、`Validate()` で検査する。仮値は設定クラスの初期値の1か所にだけ書く。
- NGO への依存は `Degunatsu.Net` に閉じ込める（Phase 2）。

### Core を書くときの制約

- Unity 6 と同じ条件でコンパイルできること: **.NET Standard 2.1、C# 9**（file-scoped namespace、record struct、global using などは使わない）。`tools/CoreTests` のビルドがこれを検査する（警告もエラー扱い）。
- テストは Unity の NUnit（3.5 相当）にある API だけを使う（`Assert.Multiple` などは使わない）。テストも UnityEngine を使わない。
- ベクトルは `System.Numerics`。Unity の型との変換は Runtime 側の境界で行う。
- `Assets/` にファイルやフォルダを追加・削除したら、`python3 tools/unity_meta.py` で `.meta` を生成し、`--check` で検査する。

## 作業の進め方（すべてのタスクで）

1. 目的と完了条件を整理する。
2. 関連する既存コードと設計書を確認する。
3. 実装方針を簡潔に説明する。
4. 必要最小限の変更を行う。
5. 実行できるテストや静的チェックを実施する。
6. 実施できなかった検証と、その理由を明記する。
7. 変更内容、残課題、次のステップを報告する。

- 大きな機能を一度に作らない。小さく作り、検証してから次へ進む。
- 仕様が未確定なら、重要度に応じて質問するか、仮定を明示して提案する。設計上の問題や矛盾に気づいたら、勝手に決めずに、問題点・選択肢・推奨案を示す。
- 既存の設計方針を変えるときは、理由と影響範囲を説明する。
- ファイルの削除、大規模なリファクタリング、パッケージの追加・更新、Unity のバージョン変更は、必要性を先に説明して承認を得る。

## 検証と報告

- **この開発環境（クラウド）では、Unity をバッチモードで動かせる**（`docs/08` 4.2）。エディタがなければ `bash tools/unity/install_editor.sh`、ライセンスは SessionStart フックが `tools/unity/activate_license.sh` で有効化する（認証に失敗したら同じ値で再試行しない）。Unity 側のコンパイルとテストは `bash tools/unity/run_unity_tests.sh`（EditMode）と `bash tools/unity/run_unity_tests.sh PlayMode` で確認する。検証用シーン（Sandbox_Wire）はエディタのビルダーで生成する（`docs/08` 4.2）。
- 画面の見た目と操作感は確認できない。確認していないものを確認済みのように報告しない。手動で行う操作と確認方法を具体的に示す。
- Core のロジックは `dotnet test tools/CoreTests/Degunatsu.Core.Tests` で検証する。クラウドセッションでは SessionStart フック（`.claude/hooks/session-start.sh`）が .NET 8 SDK を apt で導入する（Microsoft の配布元はネットワークポリシーで遮断されているが、Ubuntu のリポジトリと NuGet には接続できる）。
- GitHub Actions（`.github/workflows/core-tests.yml`）でも、push のたびに `.meta` の検査と Core のテストを実行する。
- 報告では、検証の種類を区別する: ✅ 自動テスト済み（dotnet）／ ✅ Unity のバッチモードで確認済み（クラウド）／ ✅ Unity で確認済み（ユーザーの報告による）／ ⚠️ 未検証（手順を添える）。
