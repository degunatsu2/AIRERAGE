using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Airerage.Runtime.Flow
{
    /// <summary>
    /// ゲームの流れ（docs/15 T6-1。D-54、D-66）。いつも読み込んでおく App のシーンに 1 つだけ置き、画面ごとのシーンを重ねて読み込む:
    /// - メニュー: メニューのシーン（タイトル、メインメニュー）＋背景のステージ（ステージの一覧の最初）。
    /// - トレーニング: 選んだステージのシーン＋トレーニングの試合の仕組みのシーン（ステージを先に読み込む。実行がステージの物を探すため）。
    /// - ローカル対戦（T6-5）: 選んだステージのシーン＋ローカル対戦の試合の仕組みのシーン。参加した人（機器とキャラクター）と、時間の
    ///   制限を渡す（LocalPlayers、LocalTimed）。
    /// - ネットワーク対戦（T6-7）: 接続とロビー（待機所、キャラクター選択、ステージ選択）はメニューのシーンで行い、試合は選んだ
    ///   ステージのシーン＋ネットワーク対戦の試合の仕組みのシーン（StartOnlineMatch）。接続は App のシーンの OnlineConnection が持つ。
    ///   検証用のシーン（Sandbox_Online）は StartOnline で開ける（メニューからは使わない）。
    /// 光と空はステージのシーンのものを使う（ステージのシーンを有効にする）。読み込んでいる間の操作は受け付けない。
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

        /// <summary>今の画面。</summary>
        public FlowScreen Current { get; private set; }

        /// <summary>シーンを読み込んでいる途中か。</summary>
        public bool IsLoading { get; private set; }

        /// <summary>タイトルを通ったか（メニューへ戻ったときは、タイトルを出さずにメインメニューを出す）。</summary>
        public bool TitlePassed { get; private set; }

        /// <summary>今のステージ（トレーニング中。メニューでは背景のステージ）。</summary>
        public StageDefinition CurrentStage { get; private set; }

        public StageCatalog Stages => _stages;

        /// <summary>ローカル対戦に参加した人（P1 から順。ローカル対戦を始めるまでは空）。</summary>
        public IReadOnlyList<LocalPlayerSetup> LocalPlayers => _localPlayers;

        /// <summary>ローカル対戦を時間の制限ありで始めるか。</summary>
        public bool LocalTimed { get; private set; } = true;

        /// <summary>今読み込んでいる、画面のシーンの名前（テスト用）。</summary>
        public IReadOnlyList<string> LoadedScenes => _loaded;

        private void Start()
        {
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

        /// <summary>トレーニングを始める（stage のシーンに、トレーニングの試合の仕組みを重ねる）。</summary>
        public void StartTraining(StageDefinition stage)
        {
            stage = stage != null ? stage : _stages.Get(0);
            Go(FlowScreen.Training, stage, stage.SceneName, _trainingScene);
        }

        /// <summary>ローカル対戦を始める（stage のシーンに、ローカル対戦の試合の仕組みを重ねる。参加した人と時間の制限を渡す）。</summary>
        public void StartLocal(StageDefinition stage, IReadOnlyList<LocalPlayerSetup> players, bool timed)
        {
            if (IsLoading)
            {
                Debug.LogWarning($"{name}: 読み込みの途中なので、ローカル対戦は始めない", this);
                return;
            }
            _localPlayers.Clear();
            _localPlayers.AddRange(players);
            LocalTimed = timed;
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

        /// <summary>ゲームを終える（エディタでは再生を止める）。</summary>
        public void QuitGame()
        {
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
            StartCoroutine(Load(screen, stage, scenes));
        }

        // 今の画面のシーンをすべて外してから、新しいシーンを順に重ねて読み込む（最初のシーンを有効にする。光と空のため）。
        private IEnumerator Load(FlowScreen screen, StageDefinition stage, string[] scenes)
        {
            IsLoading = true;
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
