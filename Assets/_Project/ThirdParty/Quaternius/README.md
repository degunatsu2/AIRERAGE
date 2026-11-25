# Quaternius（背景の小物）

ステージの背景に置くモデル。作者は Quaternius（https://quaternius.com/）。ライセンスは CC0 1.0（各フォルダの LICENSE_*.txt。
クレジットは任意だが、配布するゲームのクレジットに「Quaternius」と書く）。無料の Standard 版から使う物だけを入れた。

- `SciFi/`: Modular SciFi MegaKit から、研究所（SECRET LAB）の奥の壁の板・柱・小物・天井の照明（2026-11-18）。絵は 1024 に縮めた。
- `Nature/`: Stylized Nature MegaKit から、火山の火口の壁の裾の焦げた枯れ木（DeadTree_1〜5。2026-11-19）と、ビル街の手前の大通りの
  街路樹（CommonTree_1・3・5。2026-11-25）。絵は 1024 に縮めた。

元の配布物は `ThirdParty/Inbox/` の zip。OBJ と .mtl をそのまま使い（.mtl の絵の行は元のパスを外してファイル名だけにした）、材質は
`Editor/Stages/ThirdPartyProps`（`PlaceMulti`・`PlaceMtl`）が .mtl の材質の名前・色・絵から作る。
