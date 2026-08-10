using System;
using System.Collections.Generic;
using SkullDive.Core;

namespace SkullDive.Ai
{
    /// <summary>
    /// 1 人のプレイヤー + AI で 1 マッチを回すドライバ。
    /// ソロモード・CLI・バランスシミュレーションの共通の土台。
    ///
    /// 権威ある GameState を保持するのはこのクラスだけで、外部には PlayerView しか出さない。
    /// オンライン対戦のサーバも同じ構造(状態は持つ、View だけ配る)なので、
    /// UI 側のコードはソロとオンラインで共有できる。
    /// </summary>
    public sealed class SoloMatch
    {
        private readonly GameState _state;
        private readonly IRng _rng;
        private readonly AiBrain[] _brains;
        private readonly string[] _names;
        private readonly List<GameEvent> _pending = new List<GameEvent>();

        /// <summary>人間の席。-1 なら全席 AI(シミュレーション用)。</summary>
        public readonly int HumanSeat;

        /// <summary>false にすると RoundEnd で止まる。演出を挟みたい UI 側はこちらを使う。</summary>
        public bool AutoAdvanceRound = true;

        public SoloMatch(GameConfig config, AiPersonality[] opponents, int humanSeat = 0, int seed = 12345,
            string humanName = "YOU")
        {
            if (opponents == null) throw new ArgumentNullException("opponents");

            int playerCount = opponents.Length + (humanSeat >= 0 ? 1 : 0);
            _rng = new XorShiftRng(seed);
            _state = GameEngine.CreateMatch(config, playerCount, 0);
            HumanSeat = humanSeat;

            _brains = new AiBrain[playerCount];
            _names = new string[playerCount];
            int next = 0;
            for (int seat = 0; seat < playerCount; seat++)
            {
                if (seat == humanSeat)
                {
                    _names[seat] = humanName;
                    continue;
                }
                var personality = opponents[next++];
                _brains[seat] = new AiBrain(personality);
                _names[seat] = personality.Name;
            }
        }

        public GameState DebugState
        {
            get { return _state; }
        }

        public string[] Names
        {
            get { return _names; }
        }

        public bool IsOver
        {
            get { return _state.IsOver; }
        }

        public int Winner
        {
            get { return _state.Winner; }
        }

        public int RoundNumber
        {
            get { return _state.RoundNumber; }
        }

        public Phase Phase
        {
            get { return _state.Phase; }
        }

        public AiPersonality PersonalityOf(int seat)
        {
            return _brains[seat] != null ? _brains[seat].Personality : null;
        }

        public PlayerView ViewFor(int seat)
        {
            return ViewRedactor.Redact(_state, seat, _names);
        }

        /// <summary>人間プレイヤーから見た盤面。</summary>
        public PlayerView HumanView()
        {
            return ViewFor(HumanSeat);
        }

        /// <summary>
        /// 直前までに発生したイベントを取り出す(取り出すと空になる)。演出はこれを再生する。
        ///
        /// 人間プレイヤー向けに秘匿処理を済ませたものを返す。
        /// ソロであっても他人の伏せカードを表示してはいけないので、
        /// 既定を安全側にしておく(生データが必要な解析用途だけ TakeEventsUnredacted を使う)。
        /// </summary>
        public List<GameEvent> TakeEvents()
        {
            var drained = new List<GameEvent>(EventRedactor.Redact(_pending, HumanSeat));
            _pending.Clear();
            return drained;
        }

        /// <summary>秘匿処理をしていない生のイベント。リプレイ保存やデバッグ用。</summary>
        public List<GameEvent> TakeEventsUnredacted()
        {
            var drained = new List<GameEvent>(_pending);
            _pending.Clear();
            return drained;
        }

        /// <summary>人間の手番かどうか。</summary>
        public bool IsHumanTurn
        {
            get { return !_state.IsOver && GameEngine.CurrentActor(_state) == HumanSeat && HumanSeat >= 0; }
        }

        public void SubmitHumanAction(GameAction action)
        {
            if (action.Player != HumanSeat)
                throw new InvalidActionException("action does not belong to the human seat");
            GameEngine.Apply(_state, action, _rng, _pending);
        }

        /// <summary>RoundEnd で止めている場合に次ラウンドへ進める。</summary>
        public void AdvanceRound()
        {
            if (_state.Phase != Phase.RoundEnd) return;
            GameEngine.Apply(_state, GameAction.AdvanceRound(), _rng, _pending);
        }

        /// <summary>
        /// AI の手番とラウンド送りを、人間の手番かマッチ終了まで進める。
        /// 1 手だけ進めたい場合は Step を使う。
        /// </summary>
        public void RunUntilHumanTurn(int maxSteps = 10000)
        {
            for (int i = 0; i < maxSteps; i++)
            {
                if (!Step()) return;
            }
            throw new InvalidOperationException("solo match did not settle within " + maxSteps + " steps");
        }

        /// <summary>1 手進める。人間の入力待ち・マッチ終了・(AutoAdvanceRound=false 時の)RoundEnd で false。</summary>
        public bool Step()
        {
            if (_state.IsOver) return false;

            if (_state.Phase == Phase.RoundEnd)
            {
                if (!AutoAdvanceRound) return false;
                GameEngine.Apply(_state, GameAction.AdvanceRound(), _rng, _pending);
                return true;
            }

            int actor = GameEngine.CurrentActor(_state);
            if (actor == HumanSeat) return false;

            var brain = _brains[actor];
            var view = ViewFor(actor);
            var action = brain.Decide(view, _rng);
            GameEngine.Apply(_state, action, _rng, _pending);
            return true;
        }
    }
}
