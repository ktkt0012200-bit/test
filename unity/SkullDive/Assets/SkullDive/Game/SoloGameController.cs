using System.Collections.Generic;
using SkullDive.Ai;
using SkullDive.Core;
using UnityEngine;

namespace SkullDive.Game
{
    /// <summary>
    /// ソロモードの進行役。ルールには一切触らず、SoloMatch を「見せられる速さ」で進めるだけ。
    ///
    /// 1 手ずつ間隔を空けて進めるのが要点。まとめて解決してしまうと、
    /// スカルを踏んだ瞬間の緊張という本作の見せ場が消える。
    /// </summary>
    public sealed class SoloGameController : MonoBehaviour
    {
        [Header("卓")]
        [Range(2, 6)] public int Players = 4;

        [Tooltip("0 なら起動時にランダムに決める")]
        public int Seed;

        [Header("テンポ")]
        [Tooltip("AI の 1 手 / カード 1 枚めくるごとの間隔(秒)")]
        public float StepSeconds = 0.55f;

        [Tooltip("ラウンド決着を見せておく時間(秒)")]
        public float RoundEndHoldSeconds = 2.2f;

        [Header("ルール")]
        public int CrownValue = 2;
        public bool CrownCountsAsOneInOwnStack;
        public int PointsToWin = 2;

        private SoloMatch _match;
        private float _nextStepAt;

        /// <summary>直近のイベントを日本語にしたもの。UI はこれを流すだけでよい。</summary>
        public readonly List<string> Log = new List<string>();

        public bool HasMatch
        {
            get { return _match != null; }
        }

        public bool IsOver
        {
            get { return _match != null && _match.IsOver; }
        }

        public int Winner
        {
            get { return _match != null ? _match.Winner : -1; }
        }

        public PlayerView View
        {
            get { return _match != null ? _match.HumanView() : null; }
        }

        public bool WaitingForPlayer
        {
            get { return _match != null && _match.IsHumanTurn; }
        }

        public AiPersonality PersonalityOf(int seat)
        {
            return _match != null ? _match.PersonalityOf(seat) : null;
        }

        public string[] Names
        {
            get { return _match != null ? _match.Names : new string[0]; }
        }

        public void StartMatch()
        {
            var config = GameConfig.Default();
            config.CrownValue = CrownValue;
            config.CrownCountsAsOneInOwnStack = CrownCountsAsOneInOwnStack;
            config.PointsToWin = PointsToWin;

            var pool = AiPersonality.All();
            var opponents = new AiPersonality[Mathf.Clamp(Players, 2, 6) - 1];
            var table = AiPersonality.DefaultTable();
            for (int i = 0; i < opponents.Length; i++)
            {
                opponents[i] = i < table.Length ? table[i] : pool[i % pool.Length];
            }

            int seed = Seed != 0 ? Seed : Random.Range(1, int.MaxValue);
            _match = new SoloMatch(config, opponents, 0, seed);
            // 演出を挟むため、ラウンド送りは自分で制御する。
            _match.AutoAdvanceRound = false;

            Log.Clear();
            Drain();
            _nextStepAt = Time.time + StepSeconds;
        }

        /// <summary>プレイヤーの操作を反映する。</summary>
        public void Submit(GameAction action)
        {
            if (_match == null || !_match.IsHumanTurn) return;
            _match.SubmitHumanAction(action);
            Drain();
            _nextStepAt = Time.time + StepSeconds;
        }

        private void Update()
        {
            if (_match == null || _match.IsOver) return;
            if (Time.time < _nextStepAt) return;

            if (_match.Phase == Phase.RoundEnd)
            {
                _match.AdvanceRound();
                Drain();
                _nextStepAt = Time.time + StepSeconds;
                return;
            }

            // プレイヤーの手番なら入力を待つ。
            if (_match.IsHumanTurn) return;

            if (_match.Step())
            {
                Drain();
                _nextStepAt = Time.time + StepSeconds;
            }
        }

        /// <summary>発生したイベントをログに流し、ラウンド決着なら余韻の時間を取る。</summary>
        private void Drain()
        {
            var events = _match.TakeEvents();
            for (int i = 0; i < events.Count; i++)
            {
                string line = EventNarrator.Describe(events[i], _match.Names, _match.HumanSeat);
                if (line != null) Log.Add(line);
            }

            // ログが伸びすぎないように古いものから捨てる。
            while (Log.Count > 200) Log.RemoveAt(0);

            if (_match.Phase == Phase.RoundEnd)
            {
                _nextStepAt = Time.time + RoundEndHoldSeconds;
            }
        }
    }
}
