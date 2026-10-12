using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Degunatsu.Core.Simulation;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Replay
{
    /// <summary>
    /// 状態のバイト列とチェックサム（入力の記録と、PC 間の比較に使う）。
    /// フィールドを追加したのに StateCodec に加え忘れたら、ここで失敗する。
    /// </summary>
    public class StateCodecTests
    {
        // Checksum_IsStableAcrossRunsAndMachines の期待値（形式の版 2。2026-10-13、クラウドの Linux / .NET 8 で計算）。
        private const string ExpectedChecksum = "c4542d4fdc14e04c";

        [Test]
        public void EveryField_RoundTripsAndChangesTheChecksum()
        {
            ulong defaultChecksum = StateCodec.Checksum(default(CharacterState));
            List<FieldInfo[]> leaves = LeafPaths(typeof(CharacterState)).ToList();
            Assert.That(leaves.Count, Is.GreaterThan(20), "前提: 末端のフィールドを列挙できている");

            int seed = 0;
            foreach (FieldInfo[] path in leaves)
            {
                string name = string.Join(".", path.Select(f => f.Name));
                object value = NonDefaultValue(path[path.Length - 1].FieldType, ref seed);
                object boxed = default(CharacterState);
                SetPath(boxed, path, value);
                var state = (CharacterState)boxed;

                Assert.That(StateCodec.Checksum(state), Is.Not.EqualTo(defaultChecksum), $"{name} がチェックサムに含まれていない");
                object restored = StateCodec.FromBytes(StateCodec.ToBytes(state));
                Assert.That(GetPath(restored, path), Is.EqualTo(value), $"{name} が復元されない");
            }
        }

        [Test]
        public void Checksum_IsStableAcrossRunsAndMachines()
        {
            // 同じ状態からは、どの PC でも同じ値になる（形式を変えたときは、この値と FormatVersion を更新する）。
            CharacterState state = CharacterState.Spawn(new Vector3(1.5f, 2f, 0f), -1);
            state.Velocity = new Vector3(-3.25f, 7f, 0f);

            Assert.That(StateCodec.Checksum(state).ToString("x16"), Is.EqualTo(ExpectedChecksum));
        }

        [Test]
        public void FromBytes_WrongLength_Throws()
        {
            byte[] bytes = StateCodec.ToBytes(default(CharacterState));
            Array.Resize(ref bytes, bytes.Length + 1);

            Assert.Throws<FormatException>(() => StateCodec.FromBytes(bytes));
        }

        private static IEnumerable<FieldInfo[]> LeafPaths(Type type)
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Type fieldType = field.FieldType;
                if (fieldType.IsPrimitive || fieldType.IsEnum)
                {
                    yield return new[] { field };
                    continue;
                }
                foreach (FieldInfo[] child in LeafPaths(fieldType))
                {
                    yield return new[] { field }.Concat(child).ToArray();
                }
            }
        }

        private static object NonDefaultValue(Type type, ref int seed)
        {
            seed++;
            if (type == typeof(float))
            {
                return seed + 0.25f;
            }
            if (type == typeof(int))
            {
                return seed;
            }
            if (type == typeof(bool))
            {
                return true;
            }
            if (type == typeof(sbyte))
            {
                return (sbyte)(seed % 100 + 1);
            }
            if (type == typeof(byte))
            {
                return (byte)(seed % 200 + 1);
            }
            if (type.IsEnum)
            {
                Array values = Enum.GetValues(type);
                return values.GetValue(values.Length - 1);
            }
            throw new NotSupportedException($"{type} の値を作れません。テストに型を加えてください");
        }

        private static void SetPath(object target, FieldInfo[] path, object value)
        {
            if (path.Length == 1)
            {
                path[0].SetValue(target, value);
                return;
            }
            object child = path[0].GetValue(target);
            SetPath(child, path.Skip(1).ToArray(), value);
            path[0].SetValue(target, child);
        }

        private static object GetPath(object target, FieldInfo[] path)
        {
            object current = target;
            foreach (FieldInfo field in path)
            {
                current = field.GetValue(current);
            }
            return current;
        }
    }
}
