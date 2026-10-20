using System;
using System.Threading.Tasks;
using Airerage.Net;
using UnityEditor;
using UnityEngine;

namespace Airerage.Editor
{
    /// <summary>
    /// 参加コードのサービス（Unity の Relay）の設定を確かめる（docs/15 T6-10）。匿名でサインインし、ホストの割り当てと参加コードを作り、
    /// そのコードで割り当てに加わる（ゲームの通信はしない）。Unity Cloud のプロジェクトの紐づけ（ProjectSettings の cloudProjectId）と、
    /// ダッシュボードで Relay を有効にしたかが分かる。エディタではメニューの「Airerage」から。バッチモード:
    /// Unity -batchmode -nographics -projectPath . -executeMethod Airerage.Editor.OnlineServicesCheck.RunAndExit -logFile Logs/relaycheck.log
    /// （-quit は付けない。終わったら閉じる。成功で終了コード 0）。
    /// </summary>
    public static class OnlineServicesCheck
    {
        [MenuItem("Airerage/参加コードのサービス（Relay）を確かめる")]
        private static async void RunFromMenu()
        {
            string result = await Run();
            EditorUtility.DisplayDialog("Relay", result, "OK");
        }

        /// <summary>確かめて、結果のログを出してからエディタを閉じる（バッチモード用）。</summary>
        public static async void RunAndExit()
        {
            string result = await Run();
            EditorApplication.Exit(result.StartsWith("OK", StringComparison.Ordinal) ? 0 : 1);
        }

        private static async Task<string> Run()
        {
            Debug.Log($"[OnlineServicesCheck] Cloud project: {CloudProjectSettings.projectId} ({CloudProjectSettings.projectName})");
            if (string.IsNullOrEmpty(CloudProjectSettings.projectId))
            {
                return Report("NG: このエディタから Unity Cloud のプロジェクトが読めません（Unity のアカウントでサインインし、Edit > Project Settings > Services で紐づける）");
            }
            var relay = new UnityRelayBackend();
            try
            {
                RelayHostAllocation host = await relay.CreateAsync(OnlineSession.MaxPlayers - 1);
                Debug.Log($"[OnlineServicesCheck] Join code: {host.JoinCode}（Relay のサーバー {host.ServerData.Endpoint}）");
                await relay.JoinAsync(host.JoinCode);
                return Report($"OK: 匿名のサインイン、ホストの割り当て、参加コード {host.JoinCode} の作成と参加ができました");
            }
            catch (Exception e) when (e is Unity.Services.Core.RequestFailedException || e is Unity.Services.Core.ServicesInitializationException
                || e is ArgumentException)
            {
                Debug.LogException(e);
                return Report($"NG: {e.GetType().Name}: {e.Message}");
            }
        }

        private static string Report(string message)
        {
            Debug.Log($"[OnlineServicesCheck] {message}");
            return message;
        }
    }
}
