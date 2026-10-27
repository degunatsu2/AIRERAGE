# tools/audio — BGM と効果音を「コードで書いた楽譜」から書き出す

計画と進捗は `docs/18_audio_plan.md`。ここは使い方だけ。

## 導入（クラウドのセッション・Linux）

```bash
bash tools/audio/install_audio_tools.sh   # 何度実行してもよい。音源は合わせて約 5GB（/opt/airerage-audio）
```

入るもの: Surge XT 1.3.4（シンセ）、Dexed 0.9.8（DX7）、Dragonfly Reverb（残響）、sox・ffmpeg、Python の仮想環境
（`/opt/airerage-audio`。DawDreamer・pedalboard・pyloudnorm ほか。版は `requirements.txt`）、音源 3 つ
（Virtuosity Drums・Black And Blue Basses は CC0、jRhodes3d は「音楽を作るのに使うのは自由」）。音源は曲の書き出しにだけ使い、
ゲームには入れない（入るのは書き出した音だけ）。

## 書き出し

```bash
/opt/airerage-audio/bin/python tools/audio/render.py athletic          # ステージ ATHLETIC の BGM
/opt/airerage-audio/bin/python tools/audio/render.py athletic --rush   # 残り 1 分の版
/opt/airerage-audio/bin/python tools/audio/render.py athletic --stems  # 楽器ごとの音も書き出す
/opt/airerage-audio/bin/python tools/audio/render_se.py                # 効果音（sounds/ の定義すべて）
```

出力は `tools/audio/out/`（Git には入れない）: `<曲>_preview.wav`（試聴用。前奏＋本体＋少し、最後をフェードアウト）、
`<曲>_intro.ogg`（前奏。1 回だけ鳴らす）、`<曲>_loop.ogg`（本体。くり返す。つなぎ目なし）。楽器ごとの書き出しは
`out/cache/` に残し、音の並びが変わらない楽器は次から使い回す（混ぜ方だけ変えたときは速い）。

## 構成

| ファイル | 内容 |
|---|---|
| `airerage_audio/notation.py` | 旋律の書き方（`"e5:4 f#5:2 r:2 | ..."`。長さは 16 分音符の数、`|` で小節の長さを検査） |
| `airerage_audio/theory.py` | 和音の記号（`C#m9`、`B/D#` など）と声部の配置 |
| `airerage_audio/arrange.py` | 進行、ドラムのパターン、ベースの型、伴奏のリズム、パッド、アルペジオ、揺らぎ |
| `airerage_audio/sampler.py` | 録音された音源を鳴らす（ドラム・ベース・ローズ） |
| `airerage_audio/synth.py` | Surge XT を画面なしで鳴らす（音色の読み込み、MIDI のビブラート） |
| `airerage_audio/mix.py` | エフェクト、ラウドネス、残響、仕上げ（-16 LUFS、真のピーク -1 dBTP 以下）、書き出し |
| `airerage_audio/song.py` | 曲を書き出す（前奏＋本体 2 回を鳴らして 2 回目を切り出し、つなぎ目のないループにする） |
| `airerage_audio/sfx.py` | 効果音の部品（低いドン、帯域が動くノイズ） |
| `songs/` | 曲の楽譜（1 曲 1 ファイル） |
| `sounds/` | 効果音の定義 |

耳では確かめられないので、書き出すたびにラウドネス・真のピーク・オクターブごとの強さを表示する。曲の聞こえ方はユーザーに確かめてもらう。
