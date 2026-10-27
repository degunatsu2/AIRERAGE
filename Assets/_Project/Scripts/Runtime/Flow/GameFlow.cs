using System.Collections;
using System.Collections.Generic;
using Airerage.Runtime.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Airerage.Runtime.Flow
{
    /// <summary>
    /// ゲームの流れ（docs/15 T6-1。D-54、D-66）。いつも読み込んでおく App のシーンに 1 つだけ置き、画面ごとのシーンを重ねて読み込む:
    /// - メニュー: メニューのシーン（タイトル、メインメニュー）＋背景のステージ（ステージの一覧の最初）。
    /// - トレーニング: 選んだステージのシーン＋トレーニングの試合の仕組みのシーン（ステージを先に読み込む。実行がステージの物を探すため）。
    /// - ローカル対戦（T6-5）: 選んだステージのシーン＋ローカル対戦の試合の仕組みのシーン。参加した人（機器とキャラクター）と、時間の
    ///   制限を渡す（LocalPlayers、LocalTimed）。シングルモード（自分＋CPU。docs/16 T7-2）も同じ試合の仕組みで、CPU の人を入れる（LocalSingle）。
    /// - ネットワーク対戦（T6-7）: 接続とロビー（待機所、キャラクター選択、ステージ選択）はメニューのシーンで行い、試合は選んだ
    ///   ステージのシーン＋ネットワーク対戦の試合の仕組みのシーン（StartOnlineMatch）。接続は App のシーンの OnlineConnection が持つ。
    ///   検証用のシーン（Sandbox_Online）は StartOnline で開ける（メニューからは使わない）。
    /// 光と空はステージのシーンのものを使う（ステージのシーンを有効にする）。読み込んでいる間の操作は受け付けない。
    /// メニューの背景のステージは、ステージ選択でカーソルのあるステージに入れ替える（PreviewStage。カーソルが止まってから少し待って
    /// 読み込む。ユーザーの要望、2026-10-19）。入れ替えの途中に画面を移るときは、入れ替えが済んでから移る。
    /// ほかの部品は、シーンの中からこれを探して使う（静的な変数は使わない）。
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        public enum FlowScreen
        {
            None,
            Menu,
            Training,
            Local,
            Online,
            OnlineMatch,
        }

        [SerializeField] private StageCatalog _stages;
        [SerializeField] private string _menuScene = "Menu";
        [SerializeField] private string _trainingScene = "Match_Training";
        [SerializeField] private string _localScene = "Match_Local";
        [SerializeField] private string _onlineScene = "Sandbox_Online";
        [SerializeField] private string _onlineMatchScene = "Match_Online";

        private readonly List<string> _loaded = new List<string>();
        private readonly List<LocalPlayerSetup> _localPlayers = new List<LocalPlayerSetup>();
        private bool _returnToLocalSelect;

        // メニューの背景に映したいステージと、頼まれた時刻（カーソルが止まってから PreviewDelay 秒で読み込む）。
        private const float PreviewDelay = 0.25f;
        private StageDefinition _previewWanted;
        private float _previewRequestedAt;
        private bool _previewLoading;

        /// <summary>今の画面。</summary>
        public FlowScreen Current { get; private set; }

        /// <summary>シーンを読み込んでいる途中か。</summary>
        public bool IsLoading { get; private set; }

        /// <summary>向かっている画面（読み込みの途中は行き先、済んだら今の画面。メニューの BGM がこれを見る）。</summary>
        public FlowScreen Target { get; private set; }

        /// <summary>
        /// 向かっている画面のステージ（読み込みを始めたときに決まる。試合の仕組みのシーンが起動したときにはもう入っている。試合の BGM が
        /// これを見る。CurrentStage は読み込みが済んでから入る）。
        /// </summary>
        public StageDefinition TargetStage { get; private set; }

        /// <summary>タイトルを通ったか（メニューへ戻ったときは、タイトルを出さずにメインメニューを出す）。</summary>
        public bool TitlePassed { get; private set; }

        /// <summary>今のステージ（トレーニング中。メニューでは背景のステージ）。</summary>
        public StageDefinition CurrentStage { get; private set; }

        public StageCatalog Stages => _stages;

        /// <summary>ローカル対戦に参加した人（P1 から順。ローカル対戦を始めるまでは空）。</summary>
        public IReadOnlyList<LocalPlayerSetup> LocalPlayers => _localPlayers;

        /// <summary>ローカル対戦を時間の制限ありで始めるか。</summary>
        public bool LocalTimed { get; private set; } = true;

        /// <summary>
        /// 今のローカル対戦がシングルモード（自分＋CPU。docs/16 T7-2）か。結果の「キャラクター選択へ」で、シングルモードのキャラクター選択へ戻る。
        /// </summary>
        public bool LocalSingle { get; private set; }

        /// <summary>今読み込んでいる、画面のシーンの名前（テスト用）。</summary>
        public IReadOnlyList<string> LoadedScenes => _loaded;

        private void Start()
        {
            // 保存したオプションを当てる（全体の音量と、保存したことがあれば画面。docs/15 T6-9）。
            GameSettings settings = GameSettings.Load();
            settings.ApplyAudio();
            if (settings.ScreenSaved)
            {
                settings.ApplyScreen();
            }
            if (_stages == null || _stages.Count == 0)
            {
                Debug.LogError($"{name}: ステージの一覧が設定されていないか、空です", this);
                enabled = false;
                return;
            }
            ShowMenu();
        }

        /// <summary>タイトルを通った（メインメニューを出した）ことを覚える。</summary>
        public void MarkTitlePassed()
        {
            TitlePassed = true;
        }

        /// <summary>メニューへ（背景に最初のステージ）。</summary>
        public void ShowMenu()
        {
            StageDefinition background = _stages.Get(0);
            Go(FlowScreen.Menu, background, background.SceneName, _menuScene);
        }

        /// <summary>
        /// メニューの背景のステージを stage にする（ステージ選択のカーソル。null なら変えない）。すぐには読み込まず、カーソルが止まって
        /// から入れ替える。メニュー以外では何もしない。
        /// </summary>
        public void PreviewStage(StageDefinition stage)
        {
            if (stage == null || Current != FlowScreen.Menu)
            {
                return;
            }
            _previewWanted = stage;
            _previewRequestedAt = Time.unscaledTime;
        }

        /// <summary>背景のステージを入れ替えている途中か（テスト用）。</summary>
        public bool PreviewLoading => _previewLoading;

        private void Update()
        {
            if (_previewWanted == null || IsLoading || _previewLoading || Current != FlowScreen.Menu)
            {
                return;
            }
            if (_previewWanted == CurrentStage)
            {
                _previewWanted = null;
                return;
            }
            if (Time.unscaledTime - _previewRequestedAt >= PreviewDelay)
            {
                StageDefinition stage = _previewWanted;
                _previewWanted = null;
                StartCoroutine(Preview(stage));
            }
        }

        // 背景のステージだけを入れ替える（新しいステージを読み込んで有効にしてから、前のステージを外す。空が一瞬でも消えないように）。
        private IEnumerator Preview(StageDefinition stage)
        {
            _previewLoading = true;
            string previous = _loaded.Count > 0 ? _loaded[0] : null;
            AsyncOperation load = SceneManager.LoadSceneAsync(stage.SceneName, LoadSceneMode.Additive);
            if (load == null)
            {
                Debug.LogError($"{name}: シーン {stage.SceneName} を読み込めない（ビルドの設定のシーンの一覧にあるか）", this);
                _previewLoading = false;
                yield break;
            }
            yield return load;
            SceneManager.SetActiveScene(SceneManager.GetSceneByName(stage.SceneName));
            if (previous != null)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(previous);
                if (unload != null)
                {
                    yield return unload;
                }
                _loaded[0] = stage.SceneName;
            }
            else
            {
                _loaded.Insert(0, stage.SceneName);
            }
            CurrentStage = stage;
            _previewLoading = false;
        }

        /// <summary>
        /// ローカル対戦の結果から、同じ人（機器）とキャラクターでキャラクター選択へ戻る（docs/15 T6-8）。メニューを読み込み、メニューが
        /// ConsumeLocalSelectReturn でローカル対戦のキャラクター選択を出す。
        /// </summary>
        public void ReturnToLocalSelect()
        {
            _returnToLocalSelect = _localPlayers.Count > 0;
            ShowMenu();
        }

        /// <summary>メニューを、ローカル対戦のキャラクター選択から始めるか（1 回だけ true を返す）。</summary>
        public bool ConsumeLocalSelectReturn()
        {
            bool value = _returnToLocalSelect;
            _returnToLocalSelect = false;
            return value;
        }

        /// <summary>トレーニングを始める（stage のシーンに、トレーニングの試合の仕組みを重ねる）。</summary>
        public void StartTraining(StageDefinition stage)
        {
            stage = stage != null ? stage : _stages.Get(0);
            Go(FlowScreen.Training, stage, stage.SceneName, _trainingScene);
        }

        /// <summary>
        /// ローカル対戦を始める（stage のシーンに、ローカル対戦の試合の仕組みを重ねる。参加した人と時間の制限を渡す）。single ならシングルモード
        /// （CPU の人は LocalPlayerSetup.Cpu）。
        /// </summary>
        public void StartLocal(StageDefinition stage, IReadOnlyList<LocalPlayerSetup> players, bool timed, bool single = false)
        {
            if (IsLoading)
            {
                Debug.LogWarning($"{name}: 読み込みの途中なので、ローカル対戦は始めない", this);
                return;
            }
            _localPlayers.Clear();
            _localPlayers.AddRange(players);
            LocalTimed = timed;
            LocalSingle = single;
            stage = stage != null ? stage : _stages.Get(0);
            Go(FlowScreen.Local, stage, stage.SceneName, _localScene);
        }

        /// <summary>ネットワーク対戦の試合を始める（stage のシーンに、ネットワーク対戦の試合の仕組みを重ねる。T6-7）。</summary>
        public void StartOnlineMatch(StageDefinition stage)
        {
            stage = stage != null ? stage : _stages.Get(0);
            Go(FlowScreen.OnlineMatch, stage, stage.SceneName, _onlineMatchScene);
        }

        /// <summary>ネットワーク対戦を始める（今はオンラインの検証用のシーン。T6-7 でステージと分ける）。</summary>
        public void StartOnline()
        {
            Go(FlowScreen.Online, null, _onlineScene);
        }

        /// <summary>終わる操作を受け付けたか（テスト用）。</summary>
        public bool QuitRequested { get; private set; }

        /// <summary>本当に終えるか（テストでは false にして、QuitRequested だけを確かめる）。</summary>
        public bool AllowQuit { get; set; } = true;

        /// <summary>ゲームを終える（エディタでは再生を止める）。</summary>
        public void QuitGame()
        {
            QuitRequested = true;
            if (!AllowQuit)
            {
                return;
            }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Go(FlowScreen screen, StageDefinition stage, params string[] scenes)
        {
            if (IsLoading)
            {
                Debug.LogWarning($"{name}: 読み込みの途中なので、{screen} へは移らない", this);
                return;
            }
            Target = screen;
            TargetStage = stage;
            StartCoroutine(Load(screen, stage, scenes));
        }

        // 今の画面のシーンをすべて外してから、新しいシーンを順に重ねて読み込む（最初のシーンを有効にする。光と空のため）。
        private IEnumerator Load(FlowScreen screen, StageDefinition stage, string[] scenes)
        {
            IsLoading = true;
            _previewWanted = null;
            while (_previewLoading)
            {
                // 背景のステージを入れ替えている途中（済んでから外す）。
                yield return null;
            }
            for (int i = _loaded.Count - 1; i >= 0; i--)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(_loaded[i]);
                if (unload != null)
                {
                    yield return unload;
                }
            }
            _loaded.Clear();
            foreach (string sceneName in scenes)
            {
                AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                if (load == null)
                {
                    Debug.LogError($"{name}: シーン {sceneName} を読み込めない（ビルドの設定のシーンの一覧にあるか）", this);
                    continue;
                }
                yield return load;
                _loaded.Add(sceneName);
                if (_loaded.Count == 1)
                {
                    SceneManager.SetActiveScene(SceneManager.GetSceneByName(sceneName));
                }
            }
            Current = screen;
            CurrentStage = stage;
            IsLoading = false;
        }
    }
}
