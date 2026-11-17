# PLATEAU（実在の東京の街並み）

ステージの遠景（ATHLETIC・SKYLINE DAY・SKYLINE NIGHT）の真ん中の奥に置く、実在の東京の街の形。

- `Shinjuku_LOD2.obj`: 西新宿の高層ビル群（地図のメッシュ 53394525・53394526・53394535・53394536、LOD2、地面から 60m 以上の 89 棟）。
- `Tokyo_LOD1.obj`: その周り（14 区画、LOD1、25m 以上の約 1000 棟）。
- 作り方: `tools/assets/plateau_skyline.py`（面のつながりで建物に分け、高い建物だけ残し、色とテクスチャは捨てる）。元のデータは
  G空間情報センターの「3D都市モデル（Project PLATEAU）東京都23区（2020年度）」の OBJ。

## 出典（ライセンス）

出典: 国土交通省 Project PLATEAU「3D都市モデル（Project PLATEAU）東京都23区（2020年度）」（https://www.geospatial.jp/ckan/dataset/plateau-tokyo23ku）を加工して作成

PLATEAU のサイトポリシー（3. 著作権について）に従う。商用を含めて自由に使え、クレジット（上の出典）の表示が必要。
配布するゲームでは、クレジットの画面か同梱の文書にこの出典を書く（`docs/05` Phase 8 の配布の確認に入れてある）。
