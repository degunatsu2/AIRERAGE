using System;
using System.Collections.Generic;

namespace Degunatsu.Core.Common
{
    /// <summary>
    /// 調整値の検査結果をまとめ、不正な値があればまとめて例外にする。
    /// どの設定のどのフィールドが不正かをメッセージに含め、原因を追えるようにする。
    /// </summary>
    public sealed class TuningValidator
    {
        private readonly string _owner;
        private readonly List<string> _errors = new List<string>();

        public TuningValidator(string owner)
        {
            _owner = owner;
        }

        public IReadOnlyList<string> Errors => _errors;

        public void Positive(string field, float value)
        {
            if (float.IsNaN(value) || value <= 0f)
            {
                _errors.Add($"{field} は 0 より大きい値が必要です（現在: {value}）");
            }
        }

        public void NonNegative(string field, float value)
        {
            if (float.IsNaN(value) || value < 0f)
            {
                _errors.Add($"{field} は 0 以上が必要です（現在: {value}）");
            }
        }

        public void Positive(string field, int value)
        {
            if (value <= 0)
            {
                _errors.Add($"{field} は 1 以上が必要です（現在: {value}）");
            }
        }

        public void NonNegative(string field, int value)
        {
            if (value < 0)
            {
                _errors.Add($"{field} は 0 以上が必要です（現在: {value}）");
            }
        }

        public void InRange(string field, float value, float min, float max)
        {
            if (float.IsNaN(value) || value < min || value > max)
            {
                _errors.Add($"{field} は {min}〜{max} の範囲が必要です（現在: {value}）");
            }
        }

        public void AtMost(string field, float value, string limitField, float limit)
        {
            if (value > limit)
            {
                _errors.Add($"{field}（{value}）は {limitField}（{limit}）以下が必要です");
            }
        }

        public void NotNull(string field, object value)
        {
            if (value == null)
            {
                _errors.Add($"{field} が設定されていません");
            }
        }

        /// <summary>個々の項目では表せない条件（項目の組み合わせなど）の誤りを加える。</summary>
        public void Fail(string message)
        {
            _errors.Add(message);
        }

        public void Merge(TuningValidator other)
        {
            foreach (string error in other._errors)
            {
                _errors.Add($"{other._owner}: {error}");
            }
        }

        public void ThrowIfInvalid()
        {
            if (_errors.Count > 0)
            {
                throw new InvalidTuningException(_owner, _errors);
            }
        }
    }

    public sealed class InvalidTuningException : Exception
    {
        public InvalidTuningException(string owner, IReadOnlyList<string> errors)
            : base($"{owner} の調整値が不正です:\n- " + string.Join("\n- ", errors))
        {
            Owner = owner;
            Errors = errors;
        }

        public string Owner { get; }
        public IReadOnlyList<string> Errors { get; }
    }
}
