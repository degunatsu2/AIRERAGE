# Kenney（背景の建物）

ステージの背景に置く建物のモデル。作者は Kenney（www.kenney.nl）。ライセンスは CC0 1.0（各フォルダの LICENSE_*.txt。
クレジットは任意だが、配布するゲームのクレジットに「Kenney (www.kenney.nl)」と書く）。

- `CityCommercial/`: City Kit Commercial 2.1 から、ビル街（SKYLINE の昼夜）の手前の背景のビルに使う物（building-a〜n・
  building-skyscraper-a〜e・low-detail-building の一部）と色の絵 `colormap.png`。

元の配布物は `ThirdParty/Inbox/` の zip（2026-11-18 にユーザーが追加）。OBJ をそのまま使い（`mtllib` の行だけ外した。材質は
`Editor/Stages/ThirdPartyProps` が色の絵で作る。夜は窓の色の所だけを光らせる絵を同じ色の絵から作る）。置き方は `CityStages`。
