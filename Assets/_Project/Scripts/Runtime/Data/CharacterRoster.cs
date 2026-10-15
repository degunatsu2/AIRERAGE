using System.Collections.Generic;
using UnityEngine;

namespace Airerage.Runtime.Data
{
    /// <summary>
    /// 選べるキャラクターの一覧（docs/13 T5-5）。番号（0 から）でキャラクターを指す。オンラインでは、ロビーで選んだ番号をホストへ送り、
    /// ホストが番号のキャラクターの調整値を開始のメッセージで全員へ送る（番号の意味は、同じビルドどうしで同じ）。
    /// </summary>
    [CreateAssetMenu(menuName = "Airerage/Character Roster", fileName = "CharacterRoster")]
    public sealed class CharacterRoster : ScriptableObject
    {
        [SerializeField] private CharacterConfigAsset[] _characters = new CharacterConfigAsset[0];

        public int Count => _characters.Length;

        public IReadOnlyList<CharacterConfigAsset> Characters => _characters;

        /// <summary>番号 index のキャラクター（範囲の外なら最初のキャラクター。一覧が空なら null）。</summary>
        public CharacterConfigAsset Get(int index)
        {
            if (_characters.Length == 0)
            {
                return null;
            }
            return _characters[index >= 0 && index < _characters.Length ? index : 0];
        }

        /// <summary>範囲に収めた番号（範囲の外なら 0）。</summary>
        public int Clamp(int index)
        {
            return index >= 0 && index < _characters.Length ? index : 0;
        }

        /// <summary>シーンのビルダー用の設定。</summary>
        public void Configure(CharacterConfigAsset[] characters)
        {
            _characters = characters ?? new CharacterConfigAsset[0];
        }
    }
}
