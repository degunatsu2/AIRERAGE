# 21. 友達への配布と自動アップデート

2026-11-19 のユーザーの相談: 友達に遊んでもらうため、ビルドを毎回 zip で渡していて、毎回ダウンロードしてもらうのが申し訳ない。
「これさえ渡しておけば自動でアップデートできる」ものは作れるか。

## 1. 選択肢

| 案 | 友達の手間 | こちらの手間 | 費用 | 弱いところ |
|---|---|---|---|---|
| **A. itch.io の非公開ページ＋itch のアプリ（推奨）** | 初回だけ itch のアプリを入れてゲームを入れる。あとはアプリから起動するたびに自動で更新 | ビルドのたびに `butler push`（スクリプト 1 行） | 無料 | 友達に itch のアカウントが要る |
| B. 自前のランチャー（起動すると新しい版を確かめて落とし、ゲームを起動する exe） | 初回だけランチャーを受け取る | ランチャーを作って保守し、ビルドを置く場所（公開の場所）を用意する | 置き場所によっては有料 | Windows での確認がこちらではできない。置き場所が要る（リポジトリは非公開なので GitHub の Releases は友達から読めない） |
| C. Steam の Playtest | Steam から入れる。自動で更新 | Steamworks の登録と設定 | 登録料 100 ドル | 配布を始めるまでが重い |

**推奨は A**。itch.io は個人のゲームの配布のための場所で、butler（itch.io の公式のアップロードの道具）で上げたビルドは、itch のアプリが
自動で更新し、変わった所だけを落とす（差分のパッチ）。ページは「Restricted（招待した人だけ）」にでき、検索にも出ない。

## 2. 初めにやること（ユーザー。1 回だけ）

1. itch.io にアカウントを作り、Dashboard → Create new project。
   - Kind of project: Downloadable。
   - Visibility & access: **Restricted**（招待した人だけが見られる）。
2. butler を入れる（https://itch.io/docs/butler/installing.html）。PowerShell で `butler login` を 1 回。
3. 友達に渡すもの: プロジェクトの Distribute → Download keys で作った鍵（またはプロジェクトの Restricted の設定で招待）。友達は
   itch のアプリ（https://itch.io/app）を入れて鍵を受け取り、ライブラリからインストールする。

## 3. ビルドのたびにやること（ユーザー）

1. Unity の Build で Windows のビルドを作る（例: `Builds\Windows`）。
2. リポジトリのいちばん上で:
   ```
   powershell -ExecutionPolicy Bypass -File tools\release\push_itch.ps1 -BuildDir "Builds\Windows" -Target "ユーザー名/airerage:windows"
   ```
   （`tools/release/push_itch.ps1`。版の名前は日付とコミットの短い番号。中身が前と同じなら上げない `--if-changed`）
3. 友達は何もしなくてよい。itch のアプリからゲームを起動するときに新しい版へ更新される。

## 4. 注意

- 自動で更新されるのは、itch のアプリで入れて、butler で上げたビルドだけ（ブラウザで zip を落とした場合は更新されない）。
- 版の互換: オンライン対戦は全員が同じ版でないとずれる（入力のメッセージの版・状態の形式の版を確かめている）。友達の誰かが古い版の
  ままだと接続で弾かれるので、対戦の前にアプリを開いてもらう。
- クラウドからは itch.io へ上げない（ビルドは Windows の PC で作るため。butler の鍵もユーザーの PC に置く）。
- 決まったら（itch.io を使うか）`docs/02` に判断事項として記録する（未決定）。
