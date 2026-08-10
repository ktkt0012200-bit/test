using System;
using System.Collections.Generic;
using System.Threading;
using SkullDive.Core;
using SkullDive.Net;
using UnityEngine;

namespace SkullDive.Game
{
    /// <summary>
    /// オンライン対戦の進行役。
    ///
    /// ルールを一切実行しない点が重要。盤面を進めるのはサーバだけで、
    /// ここは受け取った View を表示し、操作を送るだけの薄い層に留める。
    /// ローカルで先読み実行(予測)を入れると、伏せカードを知らないクライアントでは
    /// そもそも結果を計算できないため、ズレて破綻する。
    ///
    /// 受信は別スレッドで動くので、キューから取り出す処理だけを Update で行う。
    /// </summary>
    public sealed class OnlineGameController : MonoBehaviour
    {
        [Header("接続先")]
        public string ServerUrl = "ws://127.0.0.1:5099/ws";
        public string PlayerName = "YOU";

        [Tooltip("空なら新規ルームを作る")]
        public string RoomCode = "";

        private GameClient _client;
        private CancellationTokenSource _cancellation;
        private string[] _names = new string[0];

        public readonly List<string> Log = new List<string>();

        public PlayerView View { get; private set; }
        public string Status { get; private set; } = "未接続";

        public RoomInfo Room
        {
            get { return _client != null ? _client.Room : null; }
        }

        public int Seat
        {
            get { return _client != null ? _client.Seat : -1; }
        }

        public bool IsHost
        {
            get { return Seat == 0; }
        }

        public bool IsConnected
        {
            get { return _client != null && _client.IsConnected; }
        }

        public bool WaitingForPlayer
        {
            get { return View != null && View.IsMyTurn; }
        }

        public string[] Names
        {
            get { return _names; }
        }

        public async void Connect()
        {
            if (_client != null) return;

            Status = "接続中...";
            _cancellation = new CancellationTokenSource();
            _client = new GameClient(WireCodec.Instance);

            try
            {
                await _client.ConnectAsync(new Uri(ServerUrl), RoomCode, PlayerName, null, _cancellation.Token);
                Status = "参加待ち";
                // 受信ループは投げっぱなしにし、結果は Update 側でキューから拾う。
                await _client.ReceiveLoopAsync(_cancellation.Token);
                Status = "切断";
            }
            catch (Exception ex)
            {
                Status = "接続失敗: " + ex.Message;
                Log.Add(Status);
            }
        }

        public async void SendStart()
        {
            if (_client == null) return;
            try { await _client.SendStartAsync(_cancellation.Token); }
            catch (Exception ex) { Log.Add("開始要求に失敗: " + ex.Message); }
        }

        public async void SendAddBot()
        {
            if (_client == null) return;
            try { await _client.SendAddBotAsync(_cancellation.Token); }
            catch (Exception ex) { Log.Add("bot 追加に失敗: " + ex.Message); }
        }

        public async void Submit(GameAction action)
        {
            if (_client == null) return;
            try { await _client.SendActionAsync(action, _cancellation.Token); }
            catch (Exception ex) { Log.Add("送信に失敗: " + ex.Message); }
        }

        private void Update()
        {
            if (_client == null) return;

            ServerMessage message;
            while (_client.TryDequeue(out message))
            {
                switch (message.Kind)
                {
                    case ServerMessageType.Welcome:
                        RoomCode = message.Room != null ? message.Room.Code : RoomCode;
                        Status = "ルーム " + RoomCode + " / 席 " + message.Seat;
                        RefreshNames(message.Room);
                        break;

                    case ServerMessageType.Room:
                        RefreshNames(message.Room);
                        Status = message.Room != null && message.Room.Started
                            ? "対戦中"
                            : "ルーム " + RoomCode + " (" + (message.Room != null ? message.Room.Seats.Length : 0) + " 人)";
                        break;

                    case ServerMessageType.State:
                        View = message.View;
                        Narrate(message.Events);
                        if (View != null && View.Phase == Phase.MatchEnd)
                        {
                            Status = View.Winner == Seat ? "勝ち!" : "負け";
                        }
                        break;

                    case ServerMessageType.Error:
                        Log.Add("サーバ: " + message.Error);
                        break;
                }
            }
        }

        private void RefreshNames(RoomInfo room)
        {
            if (room == null || room.Seats == null) return;
            _names = new string[room.Seats.Length];
            for (int i = 0; i < room.Seats.Length; i++) _names[i] = room.Seats[i].Name;
        }

        private void Narrate(GameEvent[] events)
        {
            if (events == null) return;
            for (int i = 0; i < events.Length; i++)
            {
                string line = EventNarrator.Describe(events[i], _names, Seat);
                if (line != null) Log.Add(line);
            }
            while (Log.Count > 200) Log.RemoveAt(0);
        }

        private void OnDestroy()
        {
            if (_cancellation != null) _cancellation.Cancel();
            if (_client != null)
            {
                _client.CloseAsync();
                _client.Dispose();
                _client = null;
            }
        }
    }
}
