# Poly Haven（地面の写真の絵）

ステージの背景の地面・岩山・雪山に貼る写真の絵。配布元は Poly Haven（https://polyhaven.com/）。ライセンスは CC0 1.0
（クレジットは任意。配布するゲームのクレジットに「Textures: Poly Haven (polyhaven.com)」と書く）。

1K の JPG の色の絵（`*_diff_1k.jpg`）と法線の絵（OpenGL 形式 `*_nor_gl_1k.jpg`）だけを使う。2026-11-19 にクラウドの環境から
Poly Haven の API（api.polyhaven.com・dl.polyhaven.org）で取得した。

| フォルダ | 使う所 |
|---|---|
| `rock_face_03/` | 火山の火口の壁・遠くの火山・縁の岩、雪山の岩の部分 |
| `snow_02/` | 雪山の雪、雪原の雪の地面と針葉樹の雪 |
| `leafy_grass/` | 陸上競技場の芝生 |
| `forest_ground_04/` | 恐怖の館の森の地面と墓場・農園の土 |
| `concrete_floor_02/` | 埠頭の岸壁の奥行き |
| `clean_asphalt/` | ビル街の手前の大通りの車道（2026-11-25。`Editor/Stages/CityStreet`） |
| `concrete_pavers/` | ビル街の手前の大通りの歩道（インターロッキングブロック。2026-11-25） |

貼り方は `Editor/Stages/TerrainSculpt`（`ApplyPhotos`・`GroundMaterial`）とシェーダー `Airerage/TerrainRock`（世界の位置から三方向に
投影し、色は台本のまま絵の明暗と凹凸だけを足す）。
