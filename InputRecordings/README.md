# InputRecordings

入力の記録（docs/06_prototype_plan.md T1-9b）を置くフォルダ。Sandbox_Wire で F5 を押すと、直近の入力がここに保存される（`rec_日時.txt`）。F6 で最新の記録を再生する。

- 不具合を見つけたら、その直後に F5 で保存し、ファイルをコミットする（またはチャットに貼る）と、同じ動きを再現できる。
- `cloud_linux_*.txt` は、クラウド（Linux）の Unity で作った記録。Windows の Test Runner で `RecordedReplayTests` を選んで実行し、一致すれば、Linux と Windows で同じ結果になっている（PC 間の決定性。docs/04_network.md 3.5）。
- シミュレーションのコードを変えると、古い記録は一致しなくなる。照合が終わった記録は消してよい。
