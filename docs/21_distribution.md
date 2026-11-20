# 21. 友達への配布と自動アップデート

2026-11-19 のユーザーの相談: 友達に遊んでもらうため、ビルドを毎回 zip で渡していて、毎回ダウンロードしてもらうのが申し訳ない。
「これさえ渡しておけば自動でアップデートできる」ものは作れるか。→ **itch.io＋butler に決定**（2026-11-20、ユーザー。D-100）。

## 1. 仕組み（全体の流れ）

```
[あなたの PC] Unity でビルド → Builds\Windows（フォルダごと）
      │  butler push（tools\release\push_itch.ps1）… 変わった所だけを送る
      ▼
[itch.io] 非公開（Restricted）のゲームのページの「windows」チャンネル
      │  itch のアプリが新しい版を見つけて、差分だけを落として更新
      ▼
[友達の PC] itch のアプリ（最初の 1 回だけ入れる）→ ライブラリから起動
```

- アップロードするのは **ビルドのフォルダ全体**（exe だけではない）。Unity の Windows のビルドは、`AIRERAGE.exe` と
  `AIRERAGE_Data` フォルダ・`UnityPlayer.dll`・`MonoBleedingEdge` フォルダなどがそろって初めて動く。exe だけを上げても起動しない。
- itch.io の Web の画面から手でファイルを上げる必要はない。**butler が上げる**（手で上げたファイルは自動で更新されない）。
- `Builds\Windows` は **Git には push しない**（`.gitignore` の `/Builds/` で除外済み。大きなバイナリをリポジトリに入れない）。

## 2. 最初に 1 回だけやること（あなた）

1. **itch.io のゲームのページ**（作成済み）を確かめる。Dashboard → ゲームの Edit game:
   - Kind of project: **Downloadable**。
   - Uploads: Web の画面で exe を上げていたら、**その exe を消す**（butler が上げたものと混ざるため）。
   - Visibility & access: **Restricted**（招待した人だけ）。Save。
2. **butler**: インストールして `butler login` 済み（済んでいる）。新しい PowerShell の窓で `butler -V` が版を出せば OK。
3. **Unity で Windows のビルドを作る**: File → Build Profiles → Windows（必要なら Switch Platform）→ Build →
   保存先に `D:\Create\AIRERAGE\Builds\Windows` を選ぶ（空のフォルダ。なければ作る）。中に `AIRERAGE.exe` と `AIRERAGE_Data` などが
   できる。
4. **上げる前の確認（送るファイルの一覧だけ。アップロードはしない）**: リポジトリのいちばん上（`D:\Create\AIRERAGE`）で
   ```
   powershell -ExecutionPolicy Bypass -File tools\release\push_itch.ps1 -BuildDir "Builds\Windows" -Target "degunatsu/airerage:windows" -DryRun
   ```
   `AIRERAGE.exe`・`AIRERAGE_Data\...`・`UnityPlayer.dll` などが並べば OK（`..._DoNotShip` のフォルダは自動で除外）。
5. **初めてのアップロード**:
   ```
   powershell -ExecutionPolicy Bypass -File tools\release\push_itch.ps1 -BuildDir "Builds\Windows" -Target "degunatsu/airerage:windows"
   ```
   初回は全部を送るので時間がかかる。最後に `butler status` の表が出て、itch.io のページの Uploads に「windows」の行ができる。
   `-Target` の `degunatsu` は itch.io のユーザー名、`airerage` はページの URL の最後の部分（`https://degunatsu.itch.io/airerage`
   なら `airerage`）、`windows` はチャンネル名（「windows」を含む名前なら Windows 用と自動で印が付く）。
6. **友達を招待する**: ゲームの Edit game → Distribute → Download keys → 人数分の鍵を作り、鍵のリンクを友達に送る。

## 3. 友達がやること（最初の 1 回だけ）

1. itch.io のアカウントを作り、itch のアプリ（https://itch.io/app）を入れてログインする。
2. 送られた鍵のリンクを開き、自分のアカウントに追加する（Claim / Link to account）。
3. itch のアプリの Library に AIRERAGE が出るので Install → Launch。**次からはアプリから起動すれば、新しい版に自動で更新される。**

## 4. 新しい版を出すたびにやること（あなた）

1. Unity でビルド（同じ `Builds\Windows` に上書き）。
2. 2 の 5 と同じコマンドを 1 回実行する。変わった所だけが送られる。
3. 友達は何もしなくてよい（アプリを開くと更新される。オンライン対戦の前にはアプリを開き直してもらうと確実）。

## 5. よくあるつまずき

| 症状 | 原因と直し方 |
|---|---|
| `'*' 演算子に続けて値の式を指定する必要があります` などの構文エラーと文字化け | 古い版のスクリプト（日本語入り）を Windows PowerShell 5.1 が Shift_JIS で読んで壊れていた。2026-11-20 に英語だけ（ASCII）に直した。`git pull` してからもう一度実行する |
| `butler was not found` | butler の入ったフォルダが PATH にない。インストールの手順のとおり PATH に足して、PowerShell の窓を開き直す |
| `No *_Data folder` | `-BuildDir` に exe ではなく、ビルドのフォルダ（exe と `AIRERAGE_Data` が入ったフォルダ）を指定する |
| 友達のアプリに出ない | ページが Restricted のときは鍵を Claim してもらう。Uploads の「windows」の行が非表示になっていないか確かめる |
| 友達の版だけ古い | itch のアプリを開き直す（起動中は更新されない） |

## 6. ほかの案（参考。選ばなかった）

| 案 | 弱いところ |
|---|---|
| 自前のランチャー（起動すると新しい版を確かめて落とす exe） | 作って保守する手間、ビルドの置き場所が要る（リポジトリは非公開で、友達からは読めない） |
| Steam の Playtest | Steamworks の登録料 100 ドルと設定の手間 |

## 7. メモ

- オンライン対戦は全員が同じ版でないとずれる（入力のメッセージの版・状態の形式の版を確かめている）。
- クラウドからは itch.io へ上げない（ビルドは Windows の PC で作るため。butler のログインもあなたの PC にある）。
- `tools/release/push_itch.ps1` は ASCII だけで書く（CI が検査する。Windows PowerShell 5.1 が BOM のない UTF-8 を Shift_JIS で読むため）。
