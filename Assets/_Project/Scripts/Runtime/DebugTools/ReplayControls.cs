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
    /// 入力の記録と再生（docs/06_prototype_plan.md T1-9b）。直近の入力を記録し続け、
    /// SaveRecording（F5）でファイルに保存し、PlayRecording（F6）で最新のファイルを再生する。
    /// 再生の最後に状態のチェックサムを記録と比べ、同じ動きになったかを表示する。
    /// 位置のリセットとタイプの切り替えで、記録はそこからやり直す（入力以外で状態や動きが変わるため）。
    /// ファイルはプロジェクト直下の InputRecordings フォルダに置く（不具合の報告に添付できるテキスト）。
    /// </summary>
    public sealed class ReplayControls : MonoBehaviour
    {
        private const string MapName = "Debug";
        private const string FolderName = "InputRecordings";
        private const float Width = 420f;

        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private InputActionAsset _inputActions;
        [Tooltip("記録しておく長さ（秒）。これより古い入力は捨てる")]
        [SerializeField] private float _recordSeconds = 60f;

        private InputActionMap _map;
        private InputAction _save;
        private InputAction _play;
        private InputRecorder _recorder;
        private Playback _playback;
        private GUIStyle _style;

        /// <summary>記録を保存するフォルダ（プロジェクト直下の InputRecordings）。</summary>
        public static string DefaultFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", FolderName));

        /// <summary>記録しているティック数。</summary>
        public int RecordedTicks => _recorder != null ? _recorder.Count : 0;

        public bool IsReplaying => _playback != null;

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
            _recorder = new InputRecorder(Math.Max(1, SimTime.SecondsToTicks(_recordSeconds)));
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
                return;
            }
            RestartRecording();
        }

        private void Update()
        {
            if (_save.WasPressedThisFrame())
            {
                SaveRecording(DefaultFolder);
            }
            if (_play.WasPressedThisFrame())
            {
                PlayLatest(DefaultFolder);
            }
        }

        /// <summary>直近の記録を folder に保存し、ファイルのパスを返す。記録がないか保存に失敗したら null。</summary>
        public string SaveRecording(string folder)
        {
            if (_recorder.Count == 0)
            {
                Message = "Nothing recorded yet";
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

        /// <summary>記録のファイルを読んで再生を始める。</summary>
        public void PlayFile(string path)
        {
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

            _runner.SetPlayerState(recording.StartState);
            _playback = new Playback(recording, Path.GetFileName(path), sameConfig);
            _runner.InputOverride = _playback;
            Message = $"Playing {_playback.FileName}" + (sameConfig ? string.Empty : " (config differs!)");
        }

        private void OnPlayerTicked(CharacterState after)
        {
            if (_playback != null && _playback.Finished)
            {
                FinishReplay(after);
            }

            // タイプが切り替わっていたら、このティックから記録し直す（切り替え前の入力は、今のタイプでは再生できない）。
            WireModelType type = _runner.PlayerConfig.Wire.ModelType;
            if (type != _recorder.WireModelType)
            {
                _recorder.Restart(_recorder.CurrentState, type);
            }
            _recorder.Add(_runner.LastInput, after);
        }

        private void OnPlayerReset()
        {
            if (_playback != null)
            {
                // 再生中に位置をリセットしたら、再生をやめる。
                _runner.InputOverride = null;
                _playback = null;
                Message = "Replay canceled";
            }
            RestartRecording();
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

        private void RestartRecording()
        {
            _recorder.Restart(_runner.PlayerState, _runner.PlayerConfig.Wire.ModelType);
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
            string status = _playback != null
                ? $"PLAYING {_playback.Played}/{_playback.Recording.Inputs.Count}"
                : $"REC {SimTime.TicksToSeconds(_recorder.Count):0.0}s (max {_recordSeconds:0}s)";
            string text = $"Replay  F5: save  F6: play newest\n{status}\n{Message}";
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
