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
- `Shinjuku_Ground.jpg`: 西新宿の地面（道路の地図。CityGML の道路〈tran、2 次メッシュ 533945〉の面を、
  緯度・経度から平面直角座標 IX 系へ変換して、真上から見た絵に塗った。中心から ±1200m・2048 四方・1 画素およそ 1.2m）。ビル街の昼夜で、建物を道路の地面の上に立たせる（2026-11-18 の要望: 背景の下側で建物が
  ぶつ切り）。作り方: `python3 -I tools/assets/plateau_roads.py 出力の名前 --cache 作業のフォルダ --mesh2 533945
  --center -12196.9 -34315.7 --half 1200 --size 2048`。元のデータは同じデータセットの CityGML。
- `Tokyo_LOD1.obj`: その周り（14 区画、LOD1〈高さの箱〉、25m 以上の約 1000 棟）。作り方: `tools/assets/plateau_skyline.py`（面のつながりで
  建物に分け、高い建物だけ残す。LOD1 にテクスチャはない）。
- ほかの地区（ビル街の昼夜の遠景。2026-11-26 の要望: 西新宿の後ろと手前の空いた所にも実在のビル群を、遠くに感じる加工と夜のネオンで）:
  `Ikebukuro`（池袋）・`Otemachi`（大手町と丸の内）・`Shiodome`（汐留）・`Shibuya`（渋谷）・`Shinagawa`（品川）の `_LOD2.obj`・
  `_LOD2_Atlas.jpg`・`_LOD2_AtlasNight.jpg`。西新宿と同じ加工（外壁の写真のアトラス・夜の窓のネオン・根元をそろえる）で、遠いので外壁は
  1 画素 1m・2048 四方。中心はいちばん高い建物、そこから半径で切り取る（`--radius`。渋谷は高すぎる 1 棟を外す `--max-height`）。
  共通の引数は `--atlas 2048 --texel 1.0 --night --snap-ground`、地区ごとに:
  - 池袋: `--mesh 53394577 53394576 53394587 --min-height 60 --radius 450`
  - 大手町と丸の内: `--mesh 53394621 53394611 53394622 --min-height 70 --radius 600`
  - 汐留: `--mesh 53393690 53393680 53393691 --min-height 70 --radius 450`
  - 渋谷: `--mesh 53393596 53393586 --min-height 55 --max-height 240 --radius 380`
  - 品川: `--mesh 53393559 53393549 --min-height 55 --radius 450`
  置き方と遠くに感じる加工（霧・もやの色）は `StageDressing.CreatePlateauDistricts`（`docs/15` 6.73）。
- 元のデータは G空間情報センターの「3D都市モデル（Project PLATEAU）東京都23区（2020年度）」の OBJ。

## 出典（ライセンス）

出典: 国土交通省 Project PLATEAU「3D都市モデル（Project PLATEAU）東京都23区（2020年度）」（https://www.geospatial.jp/ckan/dataset/plateau-tokyo23ku）を加工して作成

PLATEAU のサイトポリシー（3. 著作権について）に従う。商用を含めて自由に使え、クレジット（上の出典）の表示が必要。
配布するゲームでは、クレジットの画面か同梱の文書にこの出典を書く（`docs/05` Phase 8 の配布の確認に入れてある）。
