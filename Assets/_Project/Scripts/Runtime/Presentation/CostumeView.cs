using Airerage.Runtime.Data;
using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// キャラクターの仮の見た目（衣装。表示だけ。ユーザーの要望、2026-10-20）。シーンのビルダーが、キャラクターの箱の子に衣装ごとの
    /// 部品を作っておき（向きで左右が変わらない部品は箱の子、変わる部品は向きの印の子）、選ばれているキャラクターの衣装だけを出す。
    /// 箱が隠れている間（復帰待ち・点滅）は、箱の子の部品も隠す（向きの印の子は CharacterView が隠す）。
    /// 文字（STORM RUNNER のゼッケンの「風」）は PC の日本語の字体で書く（JapaneseFont）。
    /// 箱の形と違う衣装（JUGGERNAUT の戦車）は、箱そのものを見せない（影だけ落とす。箱の表示のオン・オフは、隠すかどうかの合図に使い続ける）。
    /// </summary>
    public sealed class CostumeView : MonoBehaviour
    {
        [Tooltip("衣装ごとの、箱の子の部品の親（CharacterCostume の番号の順。STANDARD は空）")]
        [SerializeField] private GameObject[] _bodySets = new GameObject[0];
        [Tooltip("衣装ごとの、向きの印の子の部品の親（同じ順。なければ空）")]
        [SerializeField] private GameObject[] _facingSets = new GameObject[0];
        [Tooltip("日本語の字体で書く文字（ゼッケン）")]
        [SerializeField] private TextMesh[] _japaneseTexts = new TextMesh[0];
        [Tooltip("箱の表示（これが隠れていたら衣装も隠す）")]
        [SerializeField] private Renderer _body;
        [Tooltip("衣装ごとに、箱そのものを見せない（影だけ）か（同じ順。なければ見せる）")]
        [SerializeField] private bool[] _hidesBody = new bool[0];

        private Renderer[] _bodyRenderers = new Renderer[0];
        private Font _japanese;
        private bool _shownVisible = true;

        /// <summary>今出している衣装（テスト用）。</summary>
        public CharacterCostume Current { get; private set; }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(GameObject[] bodySets, GameObject[] facingSets, TextMesh[] japaneseTexts, Renderer body, bool[] hidesBody = null)
        {
            _bodySets = bodySets;
            _facingSets = facingSets;
            _japaneseTexts = japaneseTexts;
            _body = body;
            _hidesBody = hidesBody ?? new bool[0];
        }

        private void Awake()
        {
            if (_japaneseTexts.Length > 0)
            {
                _japanese = JapaneseFont.Create(64);
                if (_japanese != null)
                {
                    foreach (TextMesh text in _japaneseTexts)
                    {
                        text.font = _japanese;
                        text.GetComponent<MeshRenderer>().sharedMaterial = _japanese.material;
                    }
                }
            }
            Show(Current, force: true);
        }

        private void OnDestroy()
        {
            if (_japanese != null)
            {
                Destroy(_japanese);
            }
        }

        /// <summary>キャラクター character の衣装を出す（null なら箱のまま）。</summary>
        public void Show(CharacterConfigAsset character)
        {
            Show(character != null ? character.Costume : CharacterCostume.Standard);
        }

        public void Show(CharacterCostume costume, bool force = false)
        {
            if (costume == Current && !force)
            {
                return;
            }
            Current = costume;
            int index = (int)costume;
            for (int i = 0; i < _bodySets.Length; i++)
            {
                if (_bodySets[i] != null)
                {
                    _bodySets[i].SetActive(i == index);
                }
            }
            for (int i = 0; i < _facingSets.Length; i++)
            {
                if (_facingSets[i] != null)
                {
                    _facingSets[i].SetActive(i == index);
                }
            }
            _bodyRenderers = index < _bodySets.Length && _bodySets[index] != null
                ? _bodySets[index].GetComponentsInChildren<Renderer>(true)
                : new Renderer[0];
            if (_body != null)
            {
                bool hide = index < _hidesBody.Length && _hidesBody[index];
                _body.shadowCastingMode = hide ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
            }
            _shownVisible = !_shownVisible;
            SyncVisibility();
        }

        private void LateUpdate()
        {
            SyncVisibility();
        }

        // 箱が隠れていたら、箱の子の部品も隠す。
        private void SyncVisibility()
        {
            bool visible = _body == null || _body.enabled;
            if (visible == _shownVisible)
            {
                return;
            }
            _shownVisible = visible;
            foreach (Renderer part in _bodyRenderers)
            {
                if (part != null)
                {
                    part.enabled = visible;
                }
            }
        }
    }
}
