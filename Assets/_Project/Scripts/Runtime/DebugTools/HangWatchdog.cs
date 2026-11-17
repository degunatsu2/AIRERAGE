using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Airerage.Runtime.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Airerage.Runtime.DebugTools
{
    /// <summary>
    /// 止まったときの見張り（2026-11-17 の指摘: 試合中にウインドウを戻すと同時に Esc を押すと「応答しません」になる。エディタでもビルドでも。
    /// コンソールには何も出ない。クラウドでは再現しない）。毎フレームの段階（PlayerLoop の Initialization・EarlyUpdate・PreUpdate・Update・
    /// PreLateUpdate・PostLateUpdate など）の始めに印を付け、別のスレッドが見張る。印が StallSeconds 秒進まなければ、どの段階で止まったか・
    /// 直前の出来事（ウインドウの出入り・Esc・シーンの読み込み）・IME の操作の途中か（ImeGuard.Busy）を、ログ（エディタは Editor.log、
    /// ビルドは Player.log）に書く。止まったままでもログのファイルには残る。ウインドウが裏にある間とエディタの一時停止の間は見張らない。
    /// いつも読み込むシーン（App）に置く。原因が分かったら外す。
    /// </summary>
    public sealed class HangWatchdog : MonoBehaviour
    {
        /// <summary>止まったとみなす秒数（「応答しません」は 5 秒ほどで出る）。</summary>
        public const float StallSeconds = 3f;

        private const int BreadcrumbCount = 10;
        private const int PollMilliseconds = 200;

        // PlayerLoop に入れた印を見分ける型。
        private struct PhaseMarker
        {
        }

        private readonly object _gate = new object();
        private readonly string[] _crumbs = new string[BreadcrumbCount];
        private int _crumbNext;
        private Thread _thread;
        private volatile bool _running;
        private int _beat;
        private volatile string _phase = string.Empty;
        private volatile int _frame;
        private volatile bool _focused = true;
        private volatile bool _editorPaused;
        private volatile string _lastReport;
        private int _reports;
        private ImeGuard _ime;
        private FontRebuildGuard _fonts;

        /// <summary>止まったと書いた回数（テスト用）。</summary>
        public int Reports => Volatile.Read(ref _reports);

        /// <summary>最後に書いた知らせ（テスト用）。</summary>
        public string LastReport => _lastReport;

        /// <summary>印が進んだ回数（テスト用。毎フレームの段階ごとに進む）。</summary>
        public int Beats => Volatile.Read(ref _beat);

        private void OnEnable()
        {
            _ime = GetComponent<ImeGuard>();
            _fonts = GetComponent<FontRebuildGuard>();
            InstallMarkers();
            SceneManager.sceneLoaded += OnSceneLoaded;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.pauseStateChanged += OnEditorPause;
            _editorPaused = UnityEditor.EditorApplication.isPaused;
#endif
            _running = true;
            _thread = new Thread(Watch) { IsBackground = true, Name = "AIRERAGE HangWatchdog" };
            _thread.Start();
        }

        private void OnDisable()
        {
            _running = false;
            _thread?.Join(PollMilliseconds * 3);
            _thread = null;
            RemoveMarkers();
            SceneManager.sceneLoaded -= OnSceneLoaded;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.pauseStateChanged -= OnEditorPause;
#endif
        }

#if UNITY_EDITOR
        private void OnEditorPause(UnityEditor.PauseState state)
        {
            _editorPaused = state == UnityEditor.PauseState.Paused;
            Note(_editorPaused ? "エディタの一時停止" : "エディタの一時停止を解除");
        }
#endif

        private void OnApplicationFocus(bool focused)
        {
            _focused = focused;
            Note(focused ? "ウインドウに戻った" : "ウインドウから離れた");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Note($"シーン {scene.name} を読み込んだ");
        }

        private void Update()
        {
            _frame = Time.frameCount;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Note("Esc");
            }
        }

        // 直前の出来事を覚える（メインのスレッドから）。
        private void Note(string text)
        {
            lock (_gate)
            {
                _crumbs[_crumbNext] = $"{Time.frameCount}F {Time.realtimeSinceStartup:F2}s {text}";
                _crumbNext = (_crumbNext + 1) % BreadcrumbCount;
            }
        }

        private void Mark(string phase)
        {
            _phase = phase;
            Interlocked.Increment(ref _beat);
        }

        // 毎フレームの段階（PlayerLoop のいちばん上の段）の始めに印を入れる。
        private void InstallMarkers()
        {
            PlayerLoopSystem loop = Strip(PlayerLoop.GetCurrentPlayerLoop());
            if (loop.subSystemList == null)
            {
                return;
            }
            var systems = (PlayerLoopSystem[])loop.subSystemList.Clone();
            for (int i = 0; i < systems.Length; i++)
            {
                string phase = systems[i].type != null ? systems[i].type.Name : $"#{i}";
                PlayerLoopSystem[] children = systems[i].subSystemList ?? new PlayerLoopSystem[0];
                var marked = new PlayerLoopSystem[children.Length + 1];
                marked[0] = new PlayerLoopSystem { type = typeof(PhaseMarker), updateDelegate = () => Mark(phase) };
                Array.Copy(children, 0, marked, 1, children.Length);
                systems[i].subSystemList = marked;
            }
            loop.subSystemList = systems;
            PlayerLoop.SetPlayerLoop(loop);
        }

        private static void RemoveMarkers()
        {
            PlayerLoop.SetPlayerLoop(Strip(PlayerLoop.GetCurrentPlayerLoop()));
        }

        // 印を除いた PlayerLoop（ほかが足した段はそのまま）。
        private static PlayerLoopSystem Strip(PlayerLoopSystem loop)
        {
            if (loop.subSystemList == null)
            {
                return loop;
            }
            var systems = (PlayerLoopSystem[])loop.subSystemList.Clone();
            for (int i = 0; i < systems.Length; i++)
            {
                PlayerLoopSystem[] children = systems[i].subSystemList;
                if (children != null)
                {
                    systems[i].subSystemList = Array.FindAll(children, c => c.type != typeof(PhaseMarker));
                }
            }
            loop.subSystemList = systems;
            return loop;
        }

        // 見張りのスレッド: 印が進まないまま StallSeconds 秒たったら 1 回書く（止まっている間は 10 秒ごとに書き足す）。
        private void Watch()
        {
            var clock = Stopwatch.StartNew();
            int lastBeat = Volatile.Read(ref _beat);
            double changedAt = 0d;
            double nextReport = StallSeconds;
            while (_running)
            {
                Thread.Sleep(PollMilliseconds);
                double now = clock.Elapsed.TotalSeconds;
                int beat = Volatile.Read(ref _beat);
                if (beat != lastBeat || !_focused || _editorPaused)
                {
                    lastBeat = beat;
                    changedAt = now;
                    nextReport = StallSeconds;
                    continue;
                }
                double stalled = now - changedAt;
                if (stalled >= nextReport)
                {
                    string report = Report(stalled);
                    _lastReport = report;
                    Interlocked.Increment(ref _reports);
                    Debug.LogWarning(report);
                    nextReport = stalled + 10d;
                }
            }
        }

        private string Report(double stalled)
        {
            var text = new StringBuilder();
            text.Append($"[HangWatchdog] メインの処理が {stalled:F1} 秒止まっている。止まった段階: {_phase}（{Hint(_phase)}）。フレーム {_frame}。");
            string ime = ReferenceEquals(_ime, null) ? null : _ime.Busy;
            text.Append(ime != null ? $" IME の操作の途中: {ime}。" : " IME の操作はしていない。");
            if (!ReferenceEquals(_fonts, null))
            {
                text.Append($" 字体の描き直し {_fonts.Refreshes} 回。");
            }
            text.Append(" 直前の出来事:");
            lock (_gate)
            {
                for (int i = 0; i < BreadcrumbCount; i++)
                {
                    string crumb = _crumbs[(_crumbNext + i) % BreadcrumbCount];
                    if (crumb != null)
                    {
                        text.Append(" / ").Append(crumb);
                    }
                }
            }
            return text.ToString();
        }

        // 段階の中で何をしているか（調べる手がかり）。
        private static string Hint(string phase)
        {
            switch (phase)
            {
                case "Initialization": return "フレームの始め";
                case "EarlyUpdate": return "OS からの入力・ウインドウの処理";
                case "FixedUpdate": return "物理";
                case "PreUpdate": return "入力（Input System）の更新";
                case "Update": return "スクリプトの Update とコルーチン";
                case "PreLateUpdate": return "スクリプトの LateUpdate";
                case "PostLateUpdate": return "描画・UI・画面への表示";
                default: return "不明";
            }
        }
    }
}
