# 08. クラウド環境で Unity Editor を動かすための設定

> ステータス（2026-10-12）: **使える状態**。ネットワーク（3.1）とライセンス情報（3.2）は設定済み。エディタ（6000.3.26f1）の導入、ライセンスの有効化、
> テンプレートからのプロジェクトの作成、バッチモードでのコンパイルと EditMode テストの実行を確認した（6章）。セットアップスクリプト（3.3）は任意。

---

## 1. 結論

- **可能**（2026-10-12 に確認）。ユーザーが次の2つを設定し、クラウド環境に Unity 6.3 LTS（Linux 版）を入れて、バッチモードで動かせた。
  1. 環境のネットワーク設定で、Unity のドメインを許可する（**設定済み**）
  2. Unity のライセンス情報を環境変数に登録する（**設定済み**。有効化を確認）
- 計算資源は足りている（2026-10-12 の確認: ディスク空き約30GB、メモリ15GB、4コア。Xvfb と git-lfs はインストール済み）。エディタは展開後に約8.7GB を使う。
- 毎回のセッションでの使い方は 4.2。

## 2. 設定後にできること・できないこと

| 項目 | 可否 |
|---|---|
| Unity でのコンパイル確認（エラー・警告） | できる |
| EditMode / PlayMode テストの実行 | できる（どちらも確認済み） |
| シーンを起動し、記録した入力やスクリプトで操作して、結果（位置、状態、ログ）を検証する | できる |
| 画面のスクリーンショット（仮想ディスプレイ Xvfb＋ソフトウェア描画） | できる（uGUI のメニューの画面。2026-10-17 に確認。ステージの 3D の画面も 2026-10-19 に確認。4.2 の `UiSnapshot`）。試合中の HUD（IMGUI）は撮れない |
| ビルドの作成（Linux 向け。Windows 向けはモジュール次第） | できる見込み |
| **操作感の評価（手触り、気持ちよさ、遅延の体感）** | **できない**。人が実際に遊ぶ必要がある |
| ゲームパッドでの操作 | できない |

つまり「作業 → 自動テスト・自動プレイ・スクリーンショットで確認 → 修正」のサイクルは回せるが、操作感の最終判断はユーザーの手動プレイテストが必要。

## 3. ユーザーが行う設定

### 3.1 ネットワーク（必須・設定済み）

環境の設定（セッションのタイトルバーにあるクラウド環境のメニュー → Edit）で、**Network access** を **Custom** にし、**Allowed domains** に次の2行を追加する。**「Also include default list of common package managers」にはチェックを入れたまま**にする（.NET の NuGet などが使えなくなるため）。

```
*.unity3d.com
*.unity.com
lfs.github.com
```

（`lfs.github.com` は Git LFS で画像などをアップロードするため。D-05）

これで、エディタの配布元（download.unity3d.com）、リリース情報（services.api.unity.com）、パッケージレジストリ（packages.unity.com など）、ライセンス・サインイン（license.unity3d.com、activation.unity3d.com、core.cloud.unity3d.com、api.unity.com、login.unity.com など）に接続できるようになる。

手順の詳細: https://code.claude.com/docs/en/cloud-environments#network-access

### 3.2 ライセンス情報（必須・設定済み）

同じ設定画面の **Environment variables** に、次の変数を登録する（`.env` 形式）。

| 変数名 | 必須 | 内容 |
|---|---|---|
| `UNITY_EMAIL` | 必須 | Unity アカウントのメールアドレス |
| `UNITY_PASSWORD` | 必須 | Unity アカウントのパスワード。`#` を含む場合はダブルクォートで囲む |
| `UNITY_LICENSE` | 任意 | Unity Hub で Personal ライセンスを有効化したPCにある `Unity_lic.ulf` の中身。複数行なのでダブルクォートで囲む。メールアドレスとパスワードでの有効化が通らなかったときの代替 |

エディタに同梱のライセンスクライアント（Unity.Licensing.Client 1.18.3）は、メールアドレスとパスワードで Personal ライセンスを取得するコマンド（`--activate-ulf`、`--activate-all --include-personal`）を持っている。`UNITY_EMAIL` と `UNITY_PASSWORD` だけで有効化できることを確認した（`tools/unity/activate_license.sh`。SessionStart フックが自動で実行する）。

`Unity_lic.ulf` の場所（Unity Hub でサインインし、Personal ライセンスを有効化した後）:
- Windows: `C:\ProgramData\Unity\Unity_lic.ulf`
- macOS: `/Library/Application Support/Unity/Unity_lic.ulf`

**注意**:
- **パスワードやライセンスをチャットに貼らないでください**。必ず環境変数に登録してください。
- 環境変数は、その環境を使う人なら誰でも読めます。自分専用の環境で使ってください。可能なら、クラウド用に別の Unity アカウント（Personal は無料）を作ることを勧めます。
- 二段階認証を有効にしたアカウントでは、コマンドラインからの有効化が通らない可能性があります。
- **Google などの外部アカウントでサインインしている Unity アカウントには、Unity 用のパスワードがありません**。その場合は Unity ID のサイトでパスワードを設定してから、そのパスワードを `UNITY_PASSWORD` に登録してください（Google のパスワードでは通りません）。
- 認証に何度も失敗するとアカウントがロックされる可能性があるため、Claude は失敗したら同じ値で再試行しません。
- クラウド環境は使うたびに新しいマシンとして扱われるため、Unity アカウントの有効化済みデバイスが増えていく可能性があります。ライセンスクライアントには返却のコマンド（`--return-ulf`、`--deactivate-all`）があるが、クラウドのマシンはセッションの終わりを待たずに回収されるため、確実には返却できない（**未解決**。有効化済みデバイスの上限に当たったら対応を相談する）。上限に達した場合は、Unity のアカウント画面から古いものを解除してください。

### 3.3 セットアップスクリプト（任意・推奨）

エディタは、セッションの中で `bash tools/unity/install_editor.sh` を実行すれば約3分で入る（6章）。ただし、新しいセッションのたびに入れ直しになる。

環境の設定画面の **Setup script** に次の内容を登録すると、最初のセッションで導入した結果がキャッシュ（ファイルシステムのスナップショット）され、次のセッションからは導入済みの状態で始まる。キャッシュされるのは、スクリプトが**約5分以内**に終わった場合だけ（2026-10-12 の計測では約3分）。キャッシュは約7日で作り直される。

```bash
#!/bin/bash
# AIRERAGE: Unity 6.3 LTS（Linux 版）を /opt/unity に入れる（docs/08_cloud_unity_setup.md 3.3）。
# 失敗してもセッションは起動させる（Unity を使わない作業はできるため。セッションの中で tools/unity/install_editor.sh を実行し直せる）
set -o pipefail
V=6000.3.26f1
URL=https://download.unity3d.com/download_unity/de8167e7440c/LinuxEditorInstaller/Unity-$V.tar.xz
D=/opt/unity/$V
if [ ! -x "$D/Editor/Unity" ]; then
  rm -rf "$D.tmp" && mkdir -p "$D.tmp" &&
    curl -fsS "$URL" | tar -I 'xz -T0' -x -C "$D.tmp" --no-same-owner &&
    mv "$D.tmp" "$D" ||
    { echo "Unity Editor の導入に失敗した" >&2; rm -rf "$D.tmp"; }
fi
exit 0
```

- バージョンを上げるときは、`V` と `URL` を書き換える（URL は `tools/unity/install_editor.sh` が使うリリース API で調べられる）。
- 展開時に xz が CRC でデータの破損を検出するため、MD5 の検査は省いている（セッションの中で使う `install_editor.sh` は MD5 も検査する）。

### 3.4 設定を変えたとき

環境変数とセットアップスクリプトは、**新しいセッション**か、休止から再開したセッションから反映されます（動いているセッションは、マシンが一度休止して再開するまで古い値のまま。数分間やり取りがないと休止する）。

## 4. Claude が行うこと（進捗）

| # | 内容 | 状態 |
|---|---|---|
| 1 | Unity 6.3 LTS の最新パッチ（Linux 版）をダウンロードして展開し、`-version` で起動を確認する | **完了**（2026-10-12、6000.3.26f1。6章） |
| 2 | ライセンスを有効化し、バッチモードで空のプロジェクトを開けるか確認する | **完了**（2026-10-12。1回目は `Invalid Credential` で失敗し、パスワードを登録し直して成功。6章） |
| 3 | 毎回のセッションで自動的に導入する方法を提案する | **提案済み**（3.3 のセットアップスクリプト） |
| 4 | D-04 の作成方法を「Claude がクラウドでテンプレートから作成する」に切り替えてよいか確認し、Unity プロジェクトを作成する | **完了**（2026-10-12。切り替えはユーザーが承認。4.1） |

### 4.1 プロジェクトの作成方法（D-04）

- テンプレートは、Unity Hub の「Universal 3D」と同じ `com.unity.template.urp-blank`。パッケージのレジストリには、エディタに同梱の版より新しい版がある。
  - 6000.3 に合う最新版は **17.1.0**（`unity: 6000.3`。URP 17.0.4、Input System 1.14.2、Test Framework 1.6.0 に依存。2026-10-12 に packages.unity.com で確認）。17.2 以降は 6000.6 以降向け。
  - エディタに同梱されている版（17.0.14）は古いので使わない。
- 手順: レジストリから 17.1.0 の `.tgz` を取得してチェックサムを確かめ、リポジトリの外に `-createProject <フォルダ> -cloneFromTemplate <.tgz>` で作成する。そこから `Assets/` の中身（`Assets/_Project/` は上書きしない）、`Packages/`、`ProjectSettings/` をリポジトリへコピーする（[09](09_unity_project_setup.md) の 3〜4 と同じ考え方）。
- 作成後に確認すること: コンパイルエラーがない、EditMode テスト（`Airerage.Core.Tests`）が通る、Active Input Handling が Input System を含む、`Library/` などがコミットされていない。
- **実施結果（2026-10-12）**: 上の手順で作成し、すべて確認した（6章）。テンプレートの内容（サンプルシーン、URP の設定、Input System のアクション）を取り込み、`productName` だけ `degunatsu` に変えた（2026-10-14 に `AIRERAGE` へ変更）。チュートリアル用の `TutorialInfo/` と `Readme.asset` は取り込まなかった（ゲームに不要で、ほかから参照されていない。アイコン画像が Git LFS の対象で、クラウドからは LFS にアップロードできないため（6章）。ユーザーの承認）。
  - 依存パッケージは、エディタが 6000.3 に合う版に解決した（URP 17.3.0、Input System 1.20.0、Test Framework 1.6.0 など。`Packages/manifest.json`）。テンプレートには Visual Scripting、Unity Version Control（collab-proxy）、Multiplayer Center なども含まれる。不要なものを外すかは別途相談する（パッケージの変更は承認が必要）。
  - URP の設定アセットは、2回目に開いたときに URP 17.3 の形式（`k_AssetVersion: 13`）へ自動で更新された。

### 4.2 毎回のセッションでの使い方

| 作業 | コマンド | 備考 |
|---|---|---|
| エディタの導入 | `bash tools/unity/install_editor.sh` | セットアップスクリプト（3.3）を登録していれば不要。`ProjectSettings/ProjectVersion.txt` のバージョンを入れる |
| ライセンスの有効化 | `bash tools/unity/activate_license.sh` | SessionStart フックが、エディタがあれば自動で実行する。有効化済みなら何もしない |
| EditMode テスト | `bash tools/unity/run_unity_tests.sh` | コンパイルとテストを行い、結果を要約する。ログと結果は `Logs/`（コミットしない）。2回目以降は約30秒、初回（`Library/` がない）は約2分 |
| PlayMode テスト | `bash tools/unity/run_unity_tests.sh PlayMode` | シーンを再生する通しの確認。約40秒 |
| テストを絞り込む | `bash tools/unity/run_unity_tests.sh EditMode <テストの名前>` | Unity の `-testFilter` に渡す。`[Explicit]` のテストは名前で指定したときだけ実行される |
| 入力の記録の照合 | `bash tools/unity/run_unity_tests.sh EditMode Airerage.Runtime.Tests.RecordedReplayTests` | `InputRecordings/` の記録を再生し、最後の状態が記録と一致するかを確かめる（別の PC で記録したファイルでの決定性の確認。docs/04 3.5） |
| 検証用シーンの作り直し | `Unity -batchmode -nographics -quit -projectPath . -executeMethod Airerage.Editor.SandboxWireSceneBuilder.Build` | `Assets/_Project/Scenes/Sandbox_Wire.unity` を作り直す（地形の配置はビルダーのコード）。エディタではメニューの「Airerage」から |
| オンライン用シーンの作り直し | `Unity -batchmode -nographics -quit -projectPath . -executeMethod Airerage.Editor.SandboxOnlineSceneBuilder.Build` | `Assets/_Project/Scenes/Sandbox_Online.unity` を作り直す（オンライン対戦用のアスレチックステージ。docs/10 T2-7、docs/11 T3-8） |
| ゲームの流れのシーンの作り直し | `Unity -batchmode -nographics -quit -projectPath . -executeMethod Airerage.Editor.FlowSceneBuilder.Build` | App・Menu・ステージ（Stage_Athletic と、追加の 5 つ。docs/15 6.12）・Match_Training・Match_Local・Match_Online（docs/15） |
| メニューの画面の画像 | `xvfb-run -a -s "-screen 0 1920x1080x24" Unity -batchmode -quit -projectPath . -force-glcore -executeMethod Airerage.Editor.UiSnapshot.CaptureMenus -logFile Logs/snapshot.log` | キャラクター選択（キャラクターごと）とローカル対戦の参加の画面を、16:9・横に長い画面・16:10 で `Logs/Snapshots/` に PNG で保存する（配置の確認。`-nographics` は付けない）。日本語の字は、PC の日本語の字体（ここでは WenQuanYi Zen Hei）で描く |
| ステージと結果の項目の画像 | 上と同じで `-executeMethod Airerage.Editor.UiSnapshot.CaptureStages`（ステージごとの全体と真ん中、ステージ選択）、`...UiSnapshot.CaptureResults`（結果の画面の項目） | ステージの空・光・台・飾りの確認（ギミックの線は実行中に作るので写らない）。docs/15 6.12、6.13 |
| 参加コードのサービス（Relay）の確認 | `Unity -batchmode -nographics -projectPath . -executeMethod Airerage.Editor.OnlineServicesCheck.RunAndExit -logFile Logs/relaycheck.log`（`-quit` は付けない） | 匿名のサインイン・割り当て・参加コード（docs/15 6.18）。**クラウドのエディタはユーザーの Unity のアカウントでサインインしていないので、Unity Cloud のプロジェクトを読めず NG になる**（ユーザーの PC のエディタのメニューで使う）。Relay のゲームの通信（UDP）もこの環境からは外へ出せない |
| キャラクターの見た目・終了の確認・予告の画像 | 上と同じで `-executeMethod Airerage.Editor.UiSnapshot.CaptureLooks` | キャラクターごとの仮の衣装（正面と斜め）、ゲームを終了する前の確認、火口と雪原の予告（黄色の斜線と警告の記号。実行中に作る物は起動の処理を呼んで作る）。docs/15 6.16 |
| 試合の中の表示の画像（THIEF の煙・透明と分身・長押しの矢印・置いたギミック） | `xvfb-run -a -s "-screen 0 1920x1080x24" Unity -batchmode -projectPath . -force-glcore -runTests -testPlatform PlayMode -testFilter Airerage.PlayMode.Tests.ThiefSnapshotTests -testResults Logs/snap-results.xml -logFile Logs/snap.log` | Sandbox_Wire（煙・透明・矢印）と、トレーニングのアスレチック・研究所（置いたジャンプ台・赤外線）を動かして、カメラの画を `Logs/Snapshots/Thief_*.png` に保存する（名前で選んだときだけ動くテスト。実行中に作る物も写る。docs/13 3.23、3.24） |
| 仮の効果音の書き出し | `Unity -batchmode -nographics -quit -projectPath . -executeMethod Airerage.Editor.ProceduralSoundExport.ExportAll [-exportDir 出力先]` | 合成している仮の効果音（`ProceduralAudio`）をすべて WAV に書き出す（初めは `Logs/ProceduralSe/`）。聞き比べと、大きさ・帯域を数値で確かめるため（`docs/18` 4.9） |
| 空中戦の検証用シーンの作り直し | `Unity -batchmode -nographics -quit -projectPath . -executeMethod Airerage.Editor.SandboxCombatSceneBuilder.Build` | `Assets/_Project/Scenes/Sandbox_Combat.unity` を作り直す（足場が少なく、外に出ると撃破。docs/11 T3-3） |

## 5. うまくいかない場合の代案

| 代案 | 内容 | 利点 | 欠点 |
|---|---|---|---|
| GitHub Actions＋GameCI | push のたびに GitHub のサーバーで Unity のテストを実行し、Claude が結果のログを読む。ライセンスは GitHub のリポジトリのシークレットに登録する | クラウド環境のネットワーク設定が不要 | 1回の実行に10分以上かかる。GitHub Actions の無料枠（非公開リポジトリは月2,000分）を消費する。スクリーンショットの確認が難しい |
| ユーザーのPCで確認 | Claude は純C#のロジックを `dotnet test` で検証し、Unity 上の確認はユーザーが手順に沿って行う | 追加の設定が不要 | 確認の往復に時間がかかる |

## 6. 確認結果（2026-10-12）

| 項目 | 結果 |
|---|---|
| ネットワーク | download.unity3d.com、services.api.unity.com、packages.unity.com、license.unity3d.com、activation.unity3d.com、core.cloud.unity3d.com、login.unity.com に接続できる。許可していないドメイン（example.com）は遮断される。api.unity.com のルートは 403（サーバー側の応答と思われる。ライセンスの有効化のときに再確認する） |
| 最新の 6.3 LTS | 6000.3.26f1（2026-10-08 公開、リビジョン de8167e7440c）。Linux 版のアーカイブは約4.5GB、展開後は約8.7GB |
| 導入（`tools/unity/install_editor.sh`） | ダウンロードしながら並列に展開して**約165秒**、MD5 一致。順に行うと約310秒（ダウンロード 61〜175秒、展開 134秒。ダウンロードの速度は回によって変わる） |
| 起動（`Unity -version`） | `6000.3.26f1` を表示して正常終了。足りない共有ライブラリはない |
| ライセンスの有効化（`Unity.Licensing.Client --activate-ulf --username --password`） | **成功**（約8秒。ライセンスは `~/.local/share/unity3d/Unity/Unity_lic.ulf`）。1回目は Unity の認証サーバーが `Invalid Credential`（401、コード 143.002）を返して失敗（終了コード 26）。通信は正常で、環境変数の値に引用符や空白などの混入はなかった。考えられる原因: パスワードの誤り、Google などの外部アカウントでのサインインのためパスワードがない、パスワードに `#` が含まれていて引用符なしで登録したため途中で切れた。ユーザーがパスワードを登録し直した後の2回目で成功した。パスワードはライセンスクライアントのログに伏せ字で記録され、ファイルには残らないことを確認した |
| ライセンスなしでのプロジェクト作成（`-batchmode -nographics -createProject`） | `No valid Unity Editor license found. Please activate your license.` で終了（終了コード 198）。ライセンス情報の登録が必要 |
| テンプレート | `com.unity.template.urp-blank` 17.1.0 をレジストリから取得し、SHA1 一致（約30MB。ダウンロード URL は別のホストへのリダイレクトなので `curl -L` が必要） |
| プロジェクトの作成（`-createProject -cloneFromTemplate`） | 成功（約310秒）。コンパイルエラー 0。音声デバイスがないため ALSA / FMOD のエラーが出るが、動作には影響しない |
| Git LFS へのアップロード（`git lfs push`） | 初めは `lfs.github.com` が環境のネットワークポリシーで遮断された（403）。ユーザーが Allowed domains に `lfs.github.com` を追加した後は、アップロードできた（24KB の画像で確認）。リポジトリには `git lfs install --local --skip-smudge` で pre-push フックを入れてある（クラウドのセッションごとに必要） |
| リポジトリのプロジェクトを開いて EditMode テスト（`-runTests -testPlatform EditMode`） | **49件すべて成功**（`Airerage.Core.Tests`、8つのテストクラス）。コンパイルエラー 0、警告 0。初回は約125秒（インポートを含む）、2回目は約26秒。既存の `.meta` は書き換えられなかった |
