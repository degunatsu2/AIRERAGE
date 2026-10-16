using System;
using System.Collections.Generic;

namespace Airerage.Core.Objects
{
    /// <summary>
    /// ワールドに置かれた物と投射物の一覧（番号の小さい順。docs/13 T5-6）。巻き戻しのため、スナップショットに写し、チェックサムに含める。
    /// 増減と変更は SkillRules と HitResolver だけが行う。
    /// </summary>
    public sealed class WorldObjects
    {
        private readonly List<WorldObject> _items = new List<WorldObject>();

        /// <summary>物の一覧（番号の小さい順）。</summary>
        public IReadOnlyList<WorldObject> Items => _items;

        public int Count => _items.Count;

        /// <summary>次に置く物の番号。</summary>
        public int NextId { get; private set; } = 1;

        public WorldObject this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }

        /// <summary>物を加え、その番号を返す（番号はここで決める）。</summary>
        public int Add(WorldObject item)
        {
            item.Id = NextId++;
            _items.Add(item);
            return item.Id;
        }

        public void RemoveAt(int index)
        {
            _items.RemoveAt(index);
        }

        /// <summary>番号 id の物の位置（なければ -1）。</summary>
        public int IndexOf(int id)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Id == id)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>持ち主 owner の、種類 kind の物の数。</summary>
        public int CountOf(WorldObjectKind kind, int owner)
        {
            int count = 0;
            foreach (WorldObject item in _items)
            {
                count += item.Kind == kind && item.Owner == owner ? 1 : 0;
            }
            return count;
        }

        /// <summary>other と同じ状態にする（スナップショットの保存と復元）。</summary>
        public void CopyFrom(WorldObjects other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }
            _items.Clear();
            _items.AddRange(other._items);
            NextId = other.NextId;
        }

        /// <summary>同じ状態か（テスト用）。</summary>
        public bool SameAs(WorldObjects other)
        {
            if (other == null || other._items.Count != _items.Count || other.NextId != NextId)
            {
                return false;
            }
            for (int i = 0; i < _items.Count; i++)
            {
                if (!_items[i].Equals(other._items[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public void Clear()
        {
            _items.Clear();
            NextId = 1;
        }
    }
}
