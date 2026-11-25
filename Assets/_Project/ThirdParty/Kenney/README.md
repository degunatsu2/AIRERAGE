# Kenney（背景の建物）

ステージの背景に置く建物のモデル。作者は Kenney（www.kenney.nl）。ライセンスは CC0 1.0（各フォルダの LICENSE_*.txt。
クレジットは任意だが、配布するゲームのクレジットに「Kenney (www.kenney.nl)」と書く）。

- `CityCommercial/`: City Kit Commercial 2.1 から、ビル街（SKYLINE の昼夜）の手前の背景のビルに使う物（building-a〜n・
  building-skyscraper-a〜e・low-detail-building の一部）と色の絵 `colormap.png`。
- 2026-11-19 にユーザーが追加した物（使う物だけ）:
  - `CityIndustrial/`（City Kit Industrial 2.0）: 埠頭の湾の向こうの工業港（倉庫・工場・煙突・タンク・給水塔・コンテナ）。
  - `Watercraft/`（Watercraft Pack）: 埠頭と雪原の海の船とブイ。
  - `Holiday/`（Holiday Kit）: 雪原の雪の木と岩。
  - `CityRoads/`（City Kit Roads）: ビル街の手前の大通りの信号（2026-11-25 から街灯は使わない。柱が太すぎたので、日本の道路照明灯に
    寄せた網を `Editor/Stages/CityStreet` が作る）。
  - `Racing/`（Racing Kit）: 陸上競技場の旗・旗の塔・テント・看板・縁石。
  - `Nature/`（Nature Kit）: 火山の溶岩の湖の縁の岩。
  - `Furniture/`（Furniture Kit）: キッチンの冷蔵庫・電子レンジ・コーヒーメーカー・観葉植物。
  - 受け取ったが今は使っていない物: Factory Kit・Space Kit・Space Station Kit・Modular Buildings（`ThirdParty/Inbox/` の zip のまま）。

元の配布物は `ThirdParty/Inbox/` の zip（2026-11-18 にユーザーが追加）。OBJ をそのまま使い（`mtllib` の行だけ外した。材質は
`Editor/Stages/ThirdPartyProps` が色の絵で作る。色の絵のないパック〈Racing・Nature・Furniture〉は .mtl の色から作る `PlaceMtl`。
夜は窓の色の所だけを光らせる絵を同じ色の絵から作る）。置き方は各ステージの飾り（`CityStages`・`StadiumStage`・`WharfStage` など）。
