using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ビル街（SKYLINE の昼夜）の手前の大通りを行き交う車と人影（2026-11-25 の要望。docs/15 6.70）。表示だけ（シミュレーションには
    /// 影響しない。乱数はこの表示の中だけの種）。
    /// - 車: 大通りは奥へ（+Z）まっすぐ延びる。日本の左側通行なので、奥へ向かう車は左（-X）の車線、手前へ来る車は右（+X）の車線を走る
    ///   （_laneX と _laneDirection。ふだんは外側の走行車線、ときどき内側の追越車線）。奥の端と手前の端で、走った距離に合わせて
    ///   現れて消える（ディザのフェード。Airerage/StreetActor の _Fade）。前の車に近づいたら速さを合わせる（追い越さない）。
    ///   夜は前照灯・尾灯・行灯が灯り、前の路面に前照灯の光が落ちる（_beams）。
    /// - 人影: 両側の歩道の歩く線（_walkX）のどこかに現れて、奥へ・手前へのどちらかへ何十 m か歩き、消える（建物に出入りするように）。
    ///   手足の振れはシェーダーが時間で動かす（_StepRate・_Phase）。
    /// 数は「重くならない程度に」: 車も人も決まった数の板（_cars・_people）を使い回す。始まったときに少し先まで進めておく（空の通りに
    /// ならないように）。
    /// </summary>
    public sealed class StreetTrafficView : MonoBehaviour
    {
        /// <summary>車の速さ（m/s。市街地の幹線道路の 40〜54 km/h）と、前の車との最小の間（m）。</summary>
        public const float CarSpeedMin = 11f;
        public const float CarSpeedMax = 15f;
        public const float CarGap = 9f;

        /// <summary>車が現れる・消える距離（m）と、人が現れる・消える時間（秒）。</summary>
        public const float CarFadeIn = 18f;
        public const float CarFadeOut = 22f;
        public const float PersonFadeSeconds = 1.1f;

        /// <summary>人の歩く速さ（m/s。健常者の平均 1.3 m/s の前後）と、一歩の長さ（m）。</summary>
        public const float WalkSpeedMin = 1.1f;
        public const float WalkSpeedMax = 1.5f;
        public const float StepLength = 0.72f;

        /// <summary>始まったときに先へ進めておく時間（秒）。</summary>
        public const float PrewarmSeconds = 40f;

        [Tooltip("道の高さ（ワールドの y）と、車と人が行き交う奥行きの範囲（ワールドの z。手前 → 奥）")]
        [SerializeField] private float _groundY = -24f;
        [Tooltip("歩道の高さ（ワールドの y。車道より縁石の分だけ高い）")]
        [SerializeField] private float _walkY = -23.85f;
        [SerializeField] private float _nearZ = 24f;
        [SerializeField] private float _farZ = 370f;
        [Tooltip("車線の中心の x と、その車線の向き（+1 は奥へ、-1 は手前へ）・走行車線か（外側。大きい車はここだけ）")]
        [SerializeField] private float[] _laneX = new float[0];
        [SerializeField] private int[] _laneDirection = new int[0];
        [SerializeField] private bool[] _laneOuter = new bool[0];
        [Tooltip("人が歩く線の x（両側の歩道）")]
        [SerializeField] private float[] _walkX = new float[0];
        [Tooltip("使い回す車の板（網はその都度差し替える）と、夜の前照灯の路面の光（車と同じ番号。昼は空）")]
        [SerializeField] private MeshRenderer[] _cars = new MeshRenderer[0];
        [SerializeField] private MeshRenderer[] _beams = new MeshRenderer[0];
        [Tooltip("車の種類の網・長さ（m）・出やすさ・車体の色を塗り分けるか（タクシーとバスは決まった色）・大きい車か")]
        [SerializeField] private Mesh[] _carMeshes = new Mesh[0];
        [SerializeField] private float[] _carLengths = new float[0];
        [SerializeField] private float[] _carWeights = new float[0];
        [SerializeField] private bool[] _carPainted = new bool[0];
        [SerializeField] private bool[] _carLarge = new bool[0];
        [Tooltip("使い回す人影の板と、人影の網（体つきの違い）")]
        [SerializeField] private MeshRenderer[] _people = new MeshRenderer[0];
        [SerializeField] private Mesh[] _personMeshes = new Mesh[0];
        [Tooltip("夜（灯りをつける）")]
        [SerializeField] private bool _night;
        [SerializeField] private int _seed = 2511;
        [Tooltip("車の現れる間隔（秒。向きごと）と、人の現れる間隔（秒）")]
        [SerializeField] private Vector2 _carInterval = new Vector2(3.5f, 8f);
        [SerializeField] private Vector2 _personInterval = new Vector2(0.9f, 2.4f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int PhaseId = Shader.PropertyToID("_Phase");
        private static readonly int StepRateId = Shader.PropertyToID("_StepRate");
        private static readonly int LightsId = Shader.PropertyToID("_Lights");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // 日本の新車の車体の色の割合（白が多く、黒・銀・灰が続く）に近い出やすさ。
        private static readonly Color[] BodyColors =
        {
            new Color(0.93f, 0.93f, 0.91f), new Color(0.97f, 0.97f, 0.97f), new Color(0.05f, 0.05f, 0.06f), new Color(0.66f, 0.68f, 0.7f),
            new Color(0.36f, 0.37f, 0.4f), new Color(0.12f, 0.17f, 0.32f), new Color(0.55f, 0.07f, 0.08f), new Color(0.72f, 0.65f, 0.52f),
            new Color(0.5f, 0.66f, 0.78f), new Color(0.32f, 0.24f, 0.18f),
        };
        private static readonly float[] BodyWeights = { 0.22f, 0.15f, 0.17f, 0.1f, 0.08f, 0.07f, 0.05f, 0.06f, 0.05f, 0.05f };

        // 服の色（暗めの色が多い。人影）。
        private static readonly Color[] ClothColors =
        {
            new Color(0.12f, 0.12f, 0.14f), new Color(0.16f, 0.2f, 0.32f), new Color(0.3f, 0.3f, 0.32f), new Color(0.55f, 0.5f, 0.42f),
            new Color(0.75f, 0.76f, 0.78f), new Color(0.35f, 0.24f, 0.2f), new Color(0.22f, 0.3f, 0.26f),
        };

        private struct Car
        {
            public bool Active;
            public int Lane;
            public int Kind;
            public float Z;
            public float StartZ;
            public float Speed;
            public float Desired;
        }

        private struct Person
        {
            public bool Active;
            public float X;
            public float Z;
            public int Direction;
            public float Speed;
            public float Remaining;
            public float Age;
            public float Phase;
            public float Height;
        }

        private Car[] _carStates = new Car[0];
        private Person[] _personStates = new Person[0];
        private float[] _nextCar = new float[2];
        private float _nextPerson;
        private float _clock;
        private System.Random _random;
        private MaterialPropertyBlock _block;

        /// <summary>今走っている車・歩いている人の数（テスト用）。</summary>
        public int ActiveCars
        {
            get
            {
                int count = 0;
                foreach (Car car in _carStates)
                {
                    count += car.Active ? 1 : 0;
                }
                return count;
            }
        }

        public int ActivePeople
        {
            get
            {
                int count = 0;
                foreach (Person person in _personStates)
                {
                    count += person.Active ? 1 : 0;
                }
                return count;
            }
        }

        /// <summary>走っている車がみな左側通行か（奥へ向かう車は -X の車線、手前へ来る車は +X の車線。テスト用）。</summary>
        public bool AllCarsKeepLeft
        {
            get
            {
                foreach (Car car in _carStates)
                {
                    if (!car.Active)
                    {
                        continue;
                    }
                    float x = _laneX[car.Lane];
                    int direction = _laneDirection[car.Lane];
                    if ((direction > 0 && x >= 0f) || (direction < 0 && x <= 0f))
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>奥へ・手前へ歩いている人がそれぞれいるか（テスト用）。</summary>
        public bool PeopleWalkBothWays
        {
            get
            {
                bool away = false;
                bool toward = false;
                foreach (Person person in _personStates)
                {
                    away |= person.Active && person.Direction > 0;
                    toward |= person.Active && person.Direction < 0;
                }
                return away && toward;
            }
        }

        public bool IsNight => _night;

        /// <summary>シーンのビルダー用の設定（Editor/Stages/CityStreet）。</summary>
        public void Configure(float groundY, float walkY, float nearZ, float farZ, float[] laneX, int[] laneDirection, bool[] laneOuter, float[] walkX,
            MeshRenderer[] cars, MeshRenderer[] beams, Mesh[] carMeshes, float[] carLengths, float[] carWeights, bool[] carPainted, bool[] carLarge,
            MeshRenderer[] people, Mesh[] personMeshes, bool night)
        {
            _groundY = groundY;
            _walkY = walkY;
            _nearZ = nearZ;
            _farZ = farZ;
            _laneX = laneX;
            _laneDirection = laneDirection;
            _laneOuter = laneOuter;
            _walkX = walkX;
            _cars = cars;
            _beams = beams;
            _carMeshes = carMeshes;
            _carLengths = carLengths;
            _carWeights = carWeights;
            _carPainted = carPainted;
            _carLarge = carLarge;
            _people = people;
            _personMeshes = personMeshes;
            _night = night;
        }

        private void Awake()
        {
            _random = new System.Random(_seed);
            _block = new MaterialPropertyBlock();
            _carStates = new Car[_cars.Length];
            _personStates = new Person[_people.Length];
            foreach (MeshRenderer car in _cars)
            {
                if (car != null)
                {
                    car.enabled = false;
                }
            }
            foreach (MeshRenderer beam in _beams)
            {
                if (beam != null)
                {
                    beam.enabled = false;
                }
            }
            foreach (MeshRenderer person in _people)
            {
                if (person != null)
                {
                    person.enabled = false;
                }
            }
            _nextCar[0] = 0f;
            _nextCar[1] = Range(0.5f, 2f);
            // 空の通りから始めない（先へ進めておく）。
            for (float t = 0f; t < PrewarmSeconds; t += 0.1f)
            {
                Step(0.1f);
            }
        }

        private void Update()
        {
            Step(Mathf.Min(Time.deltaTime, 0.1f));
            Render();
        }

        private void Step(float dt)
        {
            _clock += dt;
            StepCars(dt);
            StepPeople(dt);
        }

        private float Range(float min, float max)
        {
            return min + (float)_random.NextDouble() * (max - min);
        }

        private int Pick(float[] weights)
        {
            float total = 0f;
            foreach (float weight in weights)
            {
                total += weight;
            }
            float roll = Range(0f, total);
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= weights[i];
                if (roll <= 0f)
                {
                    return i;
                }
            }
            return weights.Length - 1;
        }

        private void StepCars(float dt)
        {
            // 向きごとに、決まった間隔で 1 台ずつ出す（奥へ: 手前の端から、手前へ: 奥の端から）。
            for (int side = 0; side < 2; side++)
            {
                int direction = side == 0 ? 1 : -1;
                if (_clock >= _nextCar[side])
                {
                    _nextCar[side] = TrySpawnCar(direction) ? _clock + Range(_carInterval.x, _carInterval.y) : _clock + 0.5f;
                }
            }
            for (int i = 0; i < _carStates.Length; i++)
            {
                ref Car car = ref _carStates[i];
                if (!car.Active)
                {
                    continue;
                }
                int direction = _laneDirection[car.Lane];
                // 前の車に近ければ、その速さに合わせる。
                car.Speed = car.Desired;
                float ahead = float.MaxValue;
                for (int j = 0; j < _carStates.Length; j++)
                {
                    if (j == i || !_carStates[j].Active || _carStates[j].Lane != car.Lane)
                    {
                        continue;
                    }
                    float gap = (_carStates[j].Z - car.Z) * direction - (_carLengths[_carStates[j].Kind] + _carLengths[car.Kind]) * 0.5f;
                    if (gap > 0f && gap < ahead)
                    {
                        ahead = gap;
                        if (gap < CarGap * 1.6f)
                        {
                            car.Speed = Mathf.Min(car.Speed, _carStates[j].Speed * Mathf.Clamp01(gap / CarGap));
                        }
                    }
                }
                car.Z += direction * car.Speed * dt;
                if ((direction > 0 && car.Z > _farZ) || (direction < 0 && car.Z < _nearZ))
                {
                    car.Active = false;
                }
            }
        }

        private bool TrySpawnCar(int direction)
        {
            int slot = -1;
            for (int i = 0; i < _carStates.Length; i++)
            {
                if (!_carStates[i].Active)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0 || _carMeshes.Length == 0)
            {
                return false;
            }
            int kind = Pick(_carWeights);
            bool large = kind < _carLarge.Length && _carLarge[kind];
            // ふだんは外側の走行車線（左の車線）、ときどき内側の追越車線。大きい車は走行車線だけ。
            bool wantOuter = large || _random.NextDouble() < 0.7;
            int lane = -1;
            for (int i = 0; i < _laneX.Length; i++)
            {
                if (_laneDirection[i] == direction && _laneOuter[i] == wantOuter)
                {
                    lane = i;
                }
            }
            if (lane < 0)
            {
                return false;
            }
            float start = direction > 0 ? _nearZ : _farZ;
            // 出す所に前の車がいれば待つ。
            foreach (Car other in _carStates)
            {
                if (other.Active && other.Lane == lane && Mathf.Abs(other.Z - start) < CarGap + _carLengths[other.Kind] + _carLengths[kind])
                {
                    return false;
                }
            }
            float desired = Range(CarSpeedMin, CarSpeedMax) * (large ? 0.85f : 1f);
            _carStates[slot] = new Car { Active = true, Lane = lane, Kind = kind, Z = start, StartZ = start, Speed = desired, Desired = desired };
            MeshRenderer renderer = _cars[slot];
            renderer.GetComponent<MeshFilter>().sharedMesh = _carMeshes[kind];
            renderer.GetPropertyBlock(_block);
            Color body = kind < _carPainted.Length && _carPainted[kind] ? BodyColors[Pick(BodyWeights)] : Color.white;
            _block.SetColor(BaseColorId, body);
            renderer.SetPropertyBlock(_block);
            return true;
        }

        private void StepPeople(float dt)
        {
            if (_clock >= _nextPerson && _walkX.Length > 0)
            {
                _nextPerson = _clock + Range(_personInterval.x, _personInterval.y);
                SpawnPerson();
            }
            for (int i = 0; i < _personStates.Length; i++)
            {
                ref Person person = ref _personStates[i];
                if (!person.Active)
                {
                    continue;
                }
                float move = person.Speed * dt;
                person.Z += person.Direction * move;
                person.Remaining -= move;
                person.Age += dt;
                if (person.Remaining <= 0f)
                {
                    person.Active = false;
                }
            }
        }

        private void SpawnPerson()
        {
            int slot = -1;
            for (int i = 0; i < _personStates.Length; i++)
            {
                if (!_personStates[i].Active)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0 || _personMeshes.Length == 0)
            {
                return;
            }
            int direction = _random.NextDouble() < 0.5 ? 1 : -1;
            float distance = Range(25f, 70f);
            float margin = 6f;
            float z = Range(_nearZ + margin, _farZ - margin);
            // 歩き終わる所が範囲を出ないように。
            float end = z + direction * distance;
            if (end > _farZ - margin || end < _nearZ + margin)
            {
                direction = -direction;
            }
            float x = _walkX[_random.Next(_walkX.Length)] + Range(-0.3f, 0.3f);
            float speed = Range(WalkSpeedMin, WalkSpeedMax);
            _personStates[slot] = new Person
            {
                Active = true, X = x, Z = z, Direction = direction, Speed = speed, Remaining = distance, Age = 0f, Phase = Range(0f, Mathf.PI * 2f),
                Height = Range(0.92f, 1.06f),
            };
            MeshRenderer renderer = _people[slot];
            renderer.GetComponent<MeshFilter>().sharedMesh = _personMeshes[_random.Next(_personMeshes.Length)];
            renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, ClothColors[_random.Next(ClothColors.Length)]);
            _block.SetFloat(PhaseId, _personStates[slot].Phase);
            _block.SetFloat(StepRateId, speed / StepLength);
            renderer.SetPropertyBlock(_block);
        }

        private void Render()
        {
            for (int i = 0; i < _carStates.Length; i++)
            {
                Car car = _carStates[i];
                MeshRenderer renderer = _cars[i];
                MeshRenderer beam = i < _beams.Length ? _beams[i] : null;
                if (!car.Active)
                {
                    renderer.enabled = false;
                    if (beam != null)
                    {
                        beam.enabled = false;
                    }
                    continue;
                }
                int direction = _laneDirection[car.Lane];
                float travelled = Mathf.Abs(car.Z - car.StartZ);
                float left = direction > 0 ? _farZ - car.Z : car.Z - _nearZ;
                float fade = Mathf.Clamp01(travelled / CarFadeIn) * Mathf.Clamp01(left / CarFadeOut);
                renderer.transform.SetPositionAndRotation(new Vector3(_laneX[car.Lane], _groundY, car.Z), Quaternion.Euler(0f, direction > 0 ? 0f : 180f, 0f));
                renderer.GetPropertyBlock(_block);
                _block.SetFloat(FadeId, fade);
                _block.SetFloat(LightsId, _night ? 1f : 0f);
                renderer.SetPropertyBlock(_block);
                renderer.enabled = fade > 0.01f;
                if (beam != null)
                {
                    beam.enabled = _night && fade > 0.01f;
                    if (beam.enabled)
                    {
                        beam.GetPropertyBlock(_block);
                        _block.SetColor(ColorId, new Color(1f, 0.94f, 0.82f, 0.4f * fade));
                        beam.SetPropertyBlock(_block);
                    }
                }
            }
            for (int i = 0; i < _personStates.Length; i++)
            {
                Person person = _personStates[i];
                MeshRenderer renderer = _people[i];
                if (!person.Active)
                {
                    renderer.enabled = false;
                    continue;
                }
                float fade = Mathf.Clamp01(person.Age / PersonFadeSeconds) * Mathf.Clamp01(person.Remaining / (person.Speed * PersonFadeSeconds));
                renderer.transform.SetPositionAndRotation(new Vector3(person.X, _walkY, person.Z), Quaternion.Euler(0f, person.Direction > 0 ? 0f : 180f, 0f));
                renderer.transform.localScale = Vector3.one * person.Height;
                renderer.GetPropertyBlock(_block);
                _block.SetFloat(FadeId, fade);
                renderer.SetPropertyBlock(_block);
                renderer.enabled = fade > 0.01f;
            }
        }
    }
}
