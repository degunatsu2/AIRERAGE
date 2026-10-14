# 09. Unity プロジェクトの作成手順（ユーザーのPCで行う場合）

> **代案の手順です**。D-04 の作成方法は「Claude がクラウド環境で作成する」に変更しました（2026-10-12。[08](08_cloud_unity_setup.md)）。
> クラウドで作成できなかった場合に、この手順（リポジトリ直下に、Unity Hub の「Universal 3D」テンプレートで作成）を使います。

リポジトリにはすでに `Assets/_Project/` などのファイルがあるため、Unity Hub で直接リポジトリのフォルダに新規作成することはできません。
**別の場所にテンプレートから作成し、必要なフォルダだけをリポジトリにコピー**します。

## 手順

1. **Git LFS を有効にする**（初回のみ）
   - Git LFS をインストールし、コマンドで `git lfs install` を実行する。
2. **リポジトリを取得する**
   - `git clone https://github.com/degunatsu2/AIRERAGE.git`
   - 作業ブランチを使う場合は `git checkout claude/game-project-foundation-20hni1`
3. **テンプレートからプロジェクトを作る**
   - Unity Hub で「New project」→ エディタのバージョンに **Unity 6.3 LTS** を選ぶ → テンプレート「**Universal 3D**」を選ぶ。
   - 場所はリポジトリとは**別のフォルダ**（例: デスクトップ）、名前は何でもよい（例: `airerage_template`）。
   - 作成が終わり、エディタが開いたら、エディタを閉じる。
4. **必要なフォルダをリポジトリにコピーする**
   - テンプレートのプロジェクトから、次の3つをリポジトリのフォルダ直下へコピーする。
     - `Assets/` の中身（リポジトリの `Assets/_Project/` は**上書きしない**。テンプレートのファイルを追加する形にする）
     - `Packages/`
     - `ProjectSettings/`
   - `Library/`、`Logs/`、`Temp/`、`UserSettings/` はコピーしない（.gitignore の対象）。
5. **リポジトリのフォルダを Unity で開く**
   - Unity Hub の「Add」→「Add project from disk」でリポジトリのフォルダを選び、開く。
   - 初回のインポートには時間がかかる。
6. **確認する**
   - Console にコンパイルエラーがないこと。
   - メニューの Window → General → Test Runner を開き、**EditMode** タブで「Run All」を実行し、`Airerage.Core.Tests` のテストがすべて通ること。
   - Project Settings → Player → Other Settings → **Active Input Handling** が「Input System Package (New)」または「Both」になっていること。
7. **コミットして push する**
   - Unity が生成した `.meta` ファイルも必ず含める。
   - コミットする前に、`Library/` などが含まれていないことを `git status` で確認する。

## 確認してほしいこと（Claude への報告）

- Unity のバージョン（例: 6000.3.x）
- Console のエラー・警告の有無（あれば全文）
- Test Runner の結果（通過数・失敗数。失敗があればメッセージ）
