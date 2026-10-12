using System;
using System.IO;
using System.Linq;
using System.Text;
using Degunatsu.Core.Common;
using Degunatsu.Core.Replay;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;
using Degunatsu.Runtime.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using SimInput = Degunatsu.Core.Input.PlayerInput;

namespace Degunatsu.Runtime.DebugTools
{
    /// <summary>
    /// 入力の記録と再生（docs/06_prototype_plan.md T1-9b）。
    /// - SaveRecording（F5 / 十字キー上）: 記録を始める。もう一度押すと記録を終えてファイルに保存する。
    /// - PlayRecording（F6 / 十字キー左）: 最新の記録を再生する（記録中なら、記録を終えて保存してから再生する）。再生中に押すと止める。
    /// 再生は記録を始めたときの状態から始め、記録した長さだけ進める。最後に状態のチェックサムを記録と比べ、
    /// 同じ動きになったかを表示する。再生中は記録しない。
    /// 記録中に位置のリセットやタイプの切り替えをすると、そこで記録を終えて保存する（入力以外で状態や動きが変わるため）。
    /// ファイルはプロジェクト直下の InputRecordings フォルダに置く（不具合の報告に添付できるテキスト）。
    /// </summary>
    public sealed class ReplayControls : MonoBehaviour
    {
        private const string MapName = "Debug";
        private const string FolderName = "InputRecordings";
        private const float Width = 460f;

        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private InputActionAsset _inputActions;
        [Tooltip("1回の記録の最大の長さ（秒）。超えたら記録を終えて保存する【仮】")]
        [SerializeField] private float _maxRecordSeconds = 300f;

        private InputActionMap _map;
        private InputAction _save;
        private InputAction _play;
        private InputRecorder _recorder;
        private bool _recording;
        private Playback _playback;
        private GUIStyle _style;

        /// <summary>記録を保存するフォルダ（プロジェクト直下の InputRecordings）。</summary>
        public static string DefaultFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", FolderName));

        public bool IsRecording => _recording;

        public bool IsReplaying => _playback != null;

        /// <summary>今の記録のティック数。</summary>
        public int RecordedTicks => _recording ? _recorder.Count : 0;

        /// <summary>最後に終わった再生で、最後の状態が記録と一致したか。まだ再生していなければ null。</summary>
        public bool? LastReplayMatched { get; private set; }

        /// <summary>最後の操作の結果（画面に表示する）。</summary>
        public string Message { get; private set; } = string.Empty;

        private void Awake()
        {
            if (_runner == null || _inputActions == null)
            {
                Debug.LogError($"{name}: SimulationRunner か InputActionAsset が設定されていません", this);
                enabled = false;
                return;
            }
            _map = _inputActions.FindActionMap(MapName, throwIfNotFound: true);
            _save = _map.FindAction("SaveRecording", throwIfNotFound: true);
            _play = _map.FindAction("PlayRecording", throwIfNotFound: true);
            _recorder = new InputRecorder(Math.Max(1, SimTime.SecondsToTicks(_maxRecordSeconds)));
        }

        private void OnEnable()
        {
            if (_runner == null)
            {
                return;
            }
            _map?.Enable();
            _runner.PlayerTicked += OnPlayerTicked;
            _runner.PlayerReset += OnPlayerReset;
        }

        private void OnDisable()
        {
            if (_runner == null)
            {
                return;
            }
            _runner.PlayerTicked -= OnPlayerTicked;
            _runner.PlayerReset -= OnPlayerReset;
        }

        private void Start()
        {
            if (!_runner.enabled)
            {
                // シミュレーションが動いていなければ、記録もしない（原因は SimulationRunner がログに出している）。
                enabled = false;
            }
        }

        private void Update()
        {
            if (_save.WasPressedThisFrame())
            {
                if (_recording)
                {
                    StopRecording(DefaultFolder);
                }
                else
                {
                    StartRecording();
                }
            }
            if (_play.WasPressedThisFrame())
            {
                if (_playback != null)
                {
                    StopReplay();
                }
                else
                {
                    if (_recording)
                    {
                        StopRecording(DefaultFolder);
                    }
                    PlayLatest(DefaultFolder);
                }
            }
        }

        /// <summary>今の状態から記録を始める。再生中は始めない。</summary>
        public void StartRecording()
        {
            if (_playback != null)
            {
                Message = "Stop the replay first (F6)";
                return;
            }
            _recorder.Restart(_runner.PlayerState, _runner.PlayerConfig.Wire.ModelType);
            _recording = true;
            Message = "Recording... (F5: stop and save)";
        }

        /// <summary>記録を終えて folder に保存し、ファイルのパスを返す。記録が空か保存に失敗したら null。</summary>
        public string StopRecording(string folder)
        {
            if (!_recording)
            {
                return null;
            }
            _recording = false;
            if (_recorder.Count == 0)
            {
                Message = "Recording stopped (nothing recorded)";
                return null;
            }
            InputRecording recording = _recorder.Build(JsonUtility.ToJson(_runner.PlayerConfig));
            string path = Path.Combine(folder, $"rec_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt");
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(path, recording.ToText(), new UTF8Encoding(false));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"{name}: 入力の記録を保存できませんでした（{path}）: {e.Message}", this);
                Message = "Save failed (see Console)";
                return null;
            }
            Message = $"Saved {Path.GetFileName(path)} ({SimTime.TicksToSeconds(recording.Inputs.Count):0.0}s)";
            Debug.Log($"{name}: 入力の記録を保存しました: {path}（{recording.Inputs.Count} ティック）", this);
            return path;
        }

        /// <summary>folder の中で最も新しい記録を再生する。</summary>
        public void PlayLatest(string folder)
        {
            string latest = Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.txt").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
            if (latest == null)
            {
                Message = $"No recording in {FolderName}/";
                return;
            }
            PlayFile(latest);
        }

        /// <summary>記録のファイルを読んで、記録を始めたときの状態から再生する。記録中は再生しない。</summary>
        public void PlayFile(string path)
        {
            if (_recording)
            {
                Message = "Stop recording first (F5)";
                return;
            }
            InputRecording recording;
            try
            {
                recording = InputRecording.Parse(File.ReadAllText(path));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException)
            {
                Debug.LogError($"{name}: 入力の記録を読めませんでした（{path}）: {e.Message}", this);
                Message = "Load failed (see Console)";
                return;
            }

            // 記録したときのタイプで再生する（調整値のアセットの値を変える。デバッグの切り替えと同じ）。
            CharacterConfig config = _runner.PlayerConfig;
            config.Wire.ModelType = recording.WireModelType;
            bool sameConfig = string.IsNullOrEmpty(recording.ConfigText) || recording.ConfigText == JsonUtility.ToJson(config);
            if (!sameConfig)
            {
                Debug.LogWarning($"{name}: 調整値が記録したときと違うので、同じ動きにならないことがあります（{path}）", this);
            }

            StopReplay();
            _runner.SetPlayerState(recording.StartState);
            _playback = new Playback(recording, Path.GetFileName(path), sameConfig);
            _runner.InputOverride = _playback;
            Message = $"Playing {_playback.FileName} (F6: stop)" + (sameConfig ? string.Empty : " (config differs!)");
        }

        /// <summary>再生を止める（その場から操作できるようになる）。</summary>
        public void StopReplay()
        {
            if (_playback == null)
            {
                return;
            }
            _runner.InputOverride = null;
            _playback = null;
            Message = "Replay stopped";
        }

        private void OnPlayerTicked(CharacterState after)
        {
            if (_playback != null)
            {
                if (_playback.Finished)
                {
                    FinishReplay(after);
                }
                return;
            }
            if (!_recording)
            {
                return;
            }

            // タイプが切り替わっていたら、切り替える前までを保存する（切り替えた後の入力は、記録のタイプでは再生できない）。
            if (_runner.PlayerConfig.Wire.ModelType != _recorder.WireModelType)
            {
                StopRecording(DefaultFolder);
                Message += " - stopped by type change";
                return;
            }
            _recorder.Add(_runner.LastInput, after);
            if (_recorder.Count >= _recorder.Capacity)
            {
                StopRecording(DefaultFolder);
                Message += " - reached the limit";
            }
        }

        private void OnPlayerReset()
        {
            // 再生を始めたとき（PlayFile）も呼ばれるが、そのときは記録も再生もしていない。
            if (_playback != null)
            {
                _runner.InputOverride = null;
                _playback = null;
                Message = "Replay canceled";
            }
            if (_recording)
            {
                StopRecording(DefaultFolder);
                Message += " - stopped by reset";
            }
        }

        private void FinishReplay(CharacterState after)
        {
            bool matched = StateCodec.Checksum(after) == _playback.Recording.FinalChecksum;
            LastReplayMatched = matched;
            Message = matched
                ? $"Replay OK: same final state ({_playback.FileName})"
                : $"Replay MISMATCH: final state differs ({_playback.FileName})";
            if (matched)
            {
                Debug.Log($"{name}: 再生の最後の状態が記録と一致しました（{_playback.FileName}）", this);
            }
            else
            {
                Debug.LogWarning($"{name}: 再生の最後の状態が記録と一致しません（{_playback.FileName}）。"
                    + (_playback.SameConfig ? "調整値は同じなので、決定性の問題の可能性があります" : "調整値が記録したときと違います"), this);
            }
            _runner.InputOverride = null;
            _playback = null;
        }

        private void OnGUI()
        {
            if (!_runner.enabled)
            {
                return;
            }
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 15 };
            }
            string status;
            if (_playback != null)
            {
                status = $"PLAYING {SimTime.TicksToSeconds(_playback.Played):0.0}s / {SimTime.TicksToSeconds(_playback.Recording.Inputs.Count):0.0}s";
            }
            else if (_recording)
            {
                status = $"REC {SimTime.TicksToSeconds(_recorder.Count):0.0}s (max {_maxRecordSeconds:0}s)";
            }
            else
            {
                status = "Idle";
            }
            string text = $"Replay  F5: start/stop rec  F6: play newest/stop\n{status}\n{Message}";
            const int lines = 3;
            float height = 12f + 21f * lines;
            GUI.Box(new Rect(10f, Screen.height - height - 10f, Width, height), text, _style);
        }

        /// <summary>再生中の記録。入力を1ティックずつ渡す。</summary>
        private sealed class Playback : ITickInputSource
        {
            public Playback(InputRecording recording, string fileName, bool sameConfig)
            {
                Recording = recording;
                FileName = fileName;
                SameConfig = sameConfig;
            }

            public InputRecording Recording { get; }
            public string FileName { get; }
            public bool SameConfig { get; }
            public int Played { get; private set; }
            public bool Finished => Played >= Recording.Inputs.Count;

            public bool TryGetNext(out SimInput input)
            {
                if (Finished)
                {
                    input = default;
                    return false;
                }
                input = Recording.Inputs[Played];
                Played++;
                return true;
            }
        }
    }
}
