# InputRecordings

入力の記録（docs/06_prototype_plan.md T1-9b）を置くフォルダ。Sandbox_Wire で F5 を押すと記録が始まり、もう一度押すと記録を終えてここに保存される（`rec_日時.txt`）。F6 で最新の記録を再生する（再生中にもう一度押すと止まる）。

- 不具合を再現したいときは、F5 で記録を始めてから操作し、不具合が起きたら F5 で保存して、ファイルをコミットする（またはチャットに貼る）。
- `windows_user_20261013_typeA.txt` は、ユーザーの Windows で記録したもの（3600 ティック）。クラウドの Linux でも最後の状態が一致した（2026-10-13）。
- `cloud_linux_*.txt` は、クラウド（Linux）の Unity で作った記録。Windows の Test Runner で `RecordedReplayTests` を選んで実行し、一致すれば、Linux と Windows で同じ結果になっている（PC 間の決定性。docs/04_network.md 3.5）。
- シミュレーションのコードを変えると、古い記録は一致しなくなる。照合が終わった記録は消してよい。
