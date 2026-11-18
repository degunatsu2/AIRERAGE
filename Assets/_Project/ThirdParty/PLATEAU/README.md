# PLATEAU（実在の東京の街並み）

ステージの遠景（ATHLETIC・SKYLINE DAY・SKYLINE NIGHT）の真ん中の奥に置く、実在の東京の街の形。

- `Shinjuku_LOD2.obj` と `Shinjuku_LOD2_Atlas.jpg`: 西新宿の高層ビル群（地図のメッシュ 53394525・53394526・53394535・53394536、LOD2、
  地面から 50m 以上の 151 棟、約 8.7 万三角形）と、外壁の写真（元のテクスチャの使っている範囲を、1 画素およそ 0.7m に縮めて 4096 四方の
  1 枚にまとめた）。形は減らしていない（2026-11-18 のユーザーの要望: ほぼシルエットに見えない、ある程度リアルな背景）。
  作り方: `tools/assets/plateau_textured.py`（配布の zip から必要な区画とテクスチャだけを取り出す。`tools/assets/remote_zip.py`）。
  `python3 -I tools/assets/plateau_textured.py 出力の名前 --cache 作業のフォルダ --mesh 53394525 53394526 53394535 53394536 --min-height 50
  --center -12196.9 -34315.7 --ground 34.9 --texel 0.6 --atlas 4096 --night --snap-ground`（`--snap-ground` で建物ごとに根元を地面に
  そろえる。土地の起伏で ±10m ほど違い、道路の地面に立たせると浮いたり沈んだりするため）
- `Shinjuku_LOD2_AtlasNight.jpg`: 夜の窓の明かり（外壁の写真の窓〈まわりより暗く青みのある所〉を見つけ、窓ごとに点ける・点けないと、
  建物ごとのネオンの色を決めた絵。同じ並び）。`plateau_textured.py ... --night` で作る（2026-11-18 の要望: 夜の明かりを窓と同期させ、
  ネオン風に光らせる）。
- `Shinjuku_Ground.jpg` と `Shinjuku_GroundNight.jpg`: 西新宿の地面（道路の地図。CityGML の道路〈tran、2 次メッシュ 533945〉の面を、
  緯度・経度から平面直角座標 IX 系へ変換して、真上から見た絵に塗った。中心から ±1200m・2048 四方・1 画素およそ 1.2m）と、夜の道路の
  明かり（街灯の橙と、ところどころの明るい点）。ビル街の昼夜で、建物を道路の地面の上に立たせる（2026-11-18 の要望: 背景の下側で建物が
  ぶつ切り）。作り方: `python3 -I tools/assets/plateau_roads.py 出力の名前 --cache 作業のフォルダ --mesh2 533945
  --center -12196.9 -34315.7 --half 1200 --size 2048`。元のデータは同じデータセットの CityGML。
- `Tokyo_LOD1.obj`: その周り（14 区画、LOD1〈高さの箱〉、25m 以上の約 1000 棟）。作り方: `tools/assets/plateau_skyline.py`（面のつながりで
  建物に分け、高い建物だけ残す。LOD1 にテクスチャはない）。
- 元のデータは G空間情報センターの「3D都市モデル（Project PLATEAU）東京都23区（2020年度）」の OBJ。

## 出典（ライセンス）

出典: 国土交通省 Project PLATEAU「3D都市モデル（Project PLATEAU）東京都23区（2020年度）」（https://www.geospatial.jp/ckan/dataset/plateau-tokyo23ku）を加工して作成

PLATEAU のサイトポリシー（3. 著作権について）に従う。商用を含めて自由に使え、クレジット（上の出典）の表示が必要。
配布するゲームでは、クレジットの画面か同梱の文書にこの出典を書く（`docs/05` Phase 8 の配布の確認に入れてある）。
