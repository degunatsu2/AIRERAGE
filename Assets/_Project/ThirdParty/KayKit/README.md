# KayKit（背景の小物）

ステージの背景に置く小物のモデル。作者は Kay Lousberg（www.kaylousberg.com）。ライセンスは CC0 1.0（各フォルダの LICENSE_*.txt。
クレジットは任意だが、配布するゲームのクレジットに「KayKit by Kay Lousberg」と書く）。

- `Halloween/`: KayKit : Halloween Bits 1.0 から、恐怖の館（MANOR）で使う物だけ（墓石・柵・枯れ木・松・かぼちゃ・ランタンなど）。
- `Restaurant/`: KayKit : Restaurant Bits 1.0 から、キッチン（KITCHEN）で使う物（瓶・木箱・鍋・皿立て・まな板・棚・換気扇など）と、
  恐怖の館のトマト農園の葉と実（lettuce・tomato）。
- `SpaceBase/`: KayKit : Space Base Bits 1.0 から、宇宙ステーションの奥の大きなステーションに使う物（モジュール・灯り・柱の塔・太陽電池パネル）。

元の配布物は `ThirdParty/Inbox/` の zip（2026-11-18 にユーザーが追加）。OBJ をそのまま使い（`mtllib` の行だけ外した。材質は
`Editor/Stages/ThirdPartyProps` が各パックの色の絵 `*_texture.png` で作る）、置き方は各ステージの飾り（`KitchenStage`・`ManorStage`・`SpaceStationStage`）。
