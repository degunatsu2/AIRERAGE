using System.Text;
using System.Threading.Tasks;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

namespace Airerage.Net
{
    /// <summary>ホストの Relay の割り当て（つなぐための情報と、ほかの人に伝える参加コード）。</summary>
    public readonly struct RelayHostAllocation
    {
        public readonly RelayServerData ServerData;
        public readonly string JoinCode;

        public RelayHostAllocation(RelayServerData serverData, string joinCode)
        {
            ServerData = serverData;
            JoinCode = joinCode;
        }
    }

    /// <summary>
    /// 参加コードでつなぐための Relay（docs/15 T6-10。D-16）。ホストは割り当てと参加コードを作り、参加する人はコードから割り当てに
    /// 加わる。通信そのものは TransportLink が Relay を通して行う。テストではサービスに問い合わせない偽物に差し替える。
    /// </summary>
    public interface IRelayBackend
    {
        /// <summary>ホストの割り当てを作る（maxClients はホストのほかに入れる人数）。失敗したら RequestFailedException など。</summary>
        Task<RelayHostAllocation> CreateAsync(int maxClients);

        /// <summary>参加コード joinCode の割り当てに加わる。失敗したら RequestFailedException など。</summary>
        Task<RelayServerData> JoinAsync(string joinCode);
    }

    /// <summary>
    /// Unity Gaming Services の Relay（Multiplayer Services のパッケージ）。初めて使うときにサービスを初期化し、匿名でサインインする
    /// （アカウントの登録は要らない）。通信は DTLS（暗号化した UDP）。プロジェクトは Unity Cloud のプロジェクト（ProjectSettings の
    /// cloudProjectId）で、ダッシュボードで Relay を有効にしておく。
    /// </summary>
    public sealed class UnityRelayBackend : IRelayBackend
    {
        private const string ConnectionType = "dtls";

        public async Task<RelayHostAllocation> CreateAsync(int maxClients)
        {
            await SignInAsync();
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxClients);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            return new RelayHostAllocation(allocation.ToRelayServerData(ConnectionType), JoinCodes.Normalize(joinCode));
        }

        public async Task<RelayServerData> JoinAsync(string joinCode)
        {
            await SignInAsync();
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            return allocation.ToRelayServerData(ConnectionType);
        }

        private static async Task SignInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }
    }

    /// <summary>参加コードの書き方をそろえる（大文字、空白とハイフンを除く）。</summary>
    public static class JoinCodes
    {
        public const int MinLength = 4;
        public const int MaxLength = 12;

        /// <summary>そろえた参加コード（英数字でないもの・長さが合わないものがあれば空）。</summary>
        public static string Normalize(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return string.Empty;
            }
            var builder = new StringBuilder(code.Length);
            foreach (char c in code)
            {
                if (c == ' ' || c == '-' || c == '\t')
                {
                    continue;
                }
                char upper = char.ToUpperInvariant(c);
                if (!((upper >= 'A' && upper <= 'Z') || (upper >= '0' && upper <= '9')))
                {
                    return string.Empty;
                }
                builder.Append(upper);
            }
            return builder.Length >= MinLength && builder.Length <= MaxLength ? builder.ToString() : string.Empty;
        }
    }
}
