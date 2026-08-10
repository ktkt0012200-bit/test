using SkullDive.Core;

namespace SkullDive.Net
{
    /// <summary>
    /// ワイヤーフォーマットの唯一の実装。サーバと Unity クライアントが同じこのコードを使う。
    ///
    /// リフレクションを使わず、フィールドを 1 つずつ手で読み書きしている。理由:
    ///  - IL2CPP のマネージドコード除去でフィールドが消えない(実機で壊れない)
    ///  - Unity に JSON パッケージを追加しなくて済む
    ///  - サーバとクライアントで実装が同一なので、書式のズレが起きえない
    ///
    /// キー名は標準的なシリアライザ (System.Text.Json / Newtonsoft) がフィールド名から
    /// 生成するものと一致させてある。テストで実際に突き合わせて確認している
    /// (デバッグ時に汎用ツールで JSON を読めるようにしておきたいため)。
    ///
    /// 知らないキーは読み飛ばすので、サーバに新しいフィールドを足しても古いクライアントは壊れない。
    /// </summary>
    public sealed class WireCodec : IMessageCodec
    {
        public static readonly WireCodec Instance = new WireCodec();

        // ---------------------------------------------------------------- client -> server

        public string Encode(ClientMessage message)
        {
            var writer = new JsonWriter();
            WriteClient(writer, message);
            return writer.ToString();
        }

        public static void WriteClient(JsonWriter writer, ClientMessage message)
        {
            writer.BeginObject();
            writer.Field("Type", message.Type);
            writer.Field("RoomCode", message.RoomCode);
            writer.Field("Name", message.Name);
            writer.Field("Token", message.Token);
            writer.Name("Action");
            WriteAction(writer, message.Action);
            writer.EndObject();
        }

        public static ClientMessage DecodeClient(string json)
        {
            var root = JsonValue.Parse(json);
            if (root.IsNull) return null;

            return new ClientMessage
            {
                Type = root["Type"].AsInt(),
                RoomCode = root["RoomCode"].AsString(),
                Name = root["Name"].AsString(),
                Token = root["Token"].AsString(),
                Action = ReadAction(root["Action"]),
            };
        }

        // ---------------------------------------------------------------- server -> client

        public static string EncodeServer(ServerMessage message)
        {
            var writer = new JsonWriter();
            writer.BeginObject();
            writer.Field("Type", message.Type);
            writer.Field("Seat", message.Seat);
            writer.Field("Token", message.Token);

            writer.Name("Room");
            WriteRoom(writer, message.Room);

            writer.Name("View");
            WriteView(writer, message.View);

            writer.Name("Events").BeginArray();
            if (message.Events != null)
            {
                for (int i = 0; i < message.Events.Length; i++) WriteEvent(writer, message.Events[i]);
            }
            writer.EndArray();

            writer.Field("TurnDeadlineMs", message.TurnDeadlineMs);
            writer.Field("Error", message.Error);
            writer.Field("ProtocolVersion", message.ProtocolVersion);
            writer.EndObject();
            return writer.ToString();
        }

        public ServerMessage DecodeServer(string json)
        {
            var root = JsonValue.Parse(json);
            if (root.IsNull) return null;

            var events = root["Events"];
            GameEvent[] parsed;
            if (events.Type == JsonValue.Kind.Array)
            {
                parsed = new GameEvent[events.Count];
                for (int i = 0; i < events.Count; i++) parsed[i] = ReadEvent(events[i]);
            }
            else
            {
                parsed = new GameEvent[0];
            }

            return new ServerMessage
            {
                Type = root["Type"].AsInt(),
                Seat = root["Seat"].AsInt(-1),
                Token = root["Token"].AsString(),
                Room = ReadRoom(root["Room"]),
                View = ReadView(root["View"]),
                Events = parsed,
                TurnDeadlineMs = root["TurnDeadlineMs"].AsInt(),
                Error = root["Error"].AsString(),
                ProtocolVersion = root["ProtocolVersion"].AsInt(),
            };
        }

        // ---------------------------------------------------------------- parts

        private static void WriteAction(JsonWriter writer, GameAction action)
        {
            writer.BeginObject();
            writer.Field("Kind", (int)action.Kind);
            writer.Field("Player", action.Player);
            writer.Field("Card", action.Card);
            writer.Field("Amount", action.Amount);
            writer.Field("Target", action.Target);
            writer.EndObject();
        }

        private static GameAction ReadAction(JsonValue value)
        {
            return new GameAction
            {
                Kind = (ActionKind)value["Kind"].AsInt(),
                Player = value["Player"].AsInt(),
                Card = value["Card"].AsInt(Cards.Hidden),
                Amount = value["Amount"].AsInt(),
                Target = value["Target"].AsInt(-1),
            };
        }

        private static void WriteEvent(JsonWriter writer, GameEvent e)
        {
            writer.BeginObject();
            writer.Field("Kind", (int)e.Kind);
            writer.Field("Player", e.Player);
            writer.Field("Target", e.Target);
            writer.Field("Card", e.Card);
            writer.Field("Amount", e.Amount);
            writer.Field("Extra", e.Extra);
            writer.EndObject();
        }

        private static GameEvent ReadEvent(JsonValue value)
        {
            return new GameEvent
            {
                Kind = (GameEventKind)value["Kind"].AsInt(),
                Player = value["Player"].AsInt(-1),
                Target = value["Target"].AsInt(-1),
                Card = value["Card"].AsInt(Cards.Hidden),
                Amount = value["Amount"].AsInt(),
                Extra = value["Extra"].AsInt(),
            };
        }

        private static void WriteConfig(JsonWriter writer, GameConfig config)
        {
            if (config == null) { writer.Null(); return; }

            writer.BeginObject();
            writer.Field("RoseCount", config.RoseCount);
            writer.Field("CrownCount", config.CrownCount);
            writer.Field("SkullCount", config.SkullCount);
            writer.Field("CrownValue", config.CrownValue);
            writer.Field("CrownCountsAsOneInOwnStack", config.CrownCountsAsOneInOwnStack);
            writer.Field("PointsToWin", config.PointsToWin);
            writer.Field("OneCardWinEnabled", config.OneCardWinEnabled);
            writer.Field("ChooseDiscardOnOwnSkull", config.ChooseDiscardOnOwnSkull);
            writer.EndObject();
        }

        private static GameConfig ReadConfig(JsonValue value)
        {
            if (value.IsNull) return null;

            var config = new GameConfig
            {
                RoseCount = value["RoseCount"].AsInt(2),
                CrownCount = value["CrownCount"].AsInt(1),
                SkullCount = value["SkullCount"].AsInt(1),
                CrownValue = value["CrownValue"].AsInt(2),
                CrownCountsAsOneInOwnStack = value["CrownCountsAsOneInOwnStack"].AsBool(),
                PointsToWin = value["PointsToWin"].AsInt(2),
                OneCardWinEnabled = value["OneCardWinEnabled"].AsBool(true),
                ChooseDiscardOnOwnSkull = value["ChooseDiscardOnOwnSkull"].AsBool(true),
            };
            return config;
        }

        private static void WriteRoom(JsonWriter writer, RoomInfo room)
        {
            if (room == null) { writer.Null(); return; }

            writer.BeginObject();
            writer.Field("Code", room.Code);
            writer.Name("Seats").BeginArray();
            if (room.Seats != null)
            {
                for (int i = 0; i < room.Seats.Length; i++)
                {
                    var seat = room.Seats[i];
                    writer.BeginObject();
                    writer.Field("Seat", seat.Seat);
                    writer.Field("Name", seat.Name);
                    writer.Field("IsBot", seat.IsBot);
                    writer.Field("Connected", seat.Connected);
                    writer.Field("PersonalityId", seat.PersonalityId);
                    writer.EndObject();
                }
            }
            writer.EndArray();
            writer.Field("Started", room.Started);
            writer.Field("HostSeat", room.HostSeat);
            writer.Name("Config");
            WriteConfig(writer, room.Config);
            writer.EndObject();
        }

        private static RoomInfo ReadRoom(JsonValue value)
        {
            if (value.IsNull) return null;

            var seatsValue = value["Seats"];
            var seats = new SeatInfo[seatsValue.Type == JsonValue.Kind.Array ? seatsValue.Count : 0];
            for (int i = 0; i < seats.Length; i++)
            {
                var seat = seatsValue[i];
                seats[i] = new SeatInfo
                {
                    Seat = seat["Seat"].AsInt(),
                    Name = seat["Name"].AsString(),
                    IsBot = seat["IsBot"].AsBool(),
                    Connected = seat["Connected"].AsBool(),
                    PersonalityId = seat["PersonalityId"].AsString(),
                };
            }

            return new RoomInfo
            {
                Code = value["Code"].AsString(),
                Seats = seats,
                Started = value["Started"].AsBool(),
                HostSeat = value["HostSeat"].AsInt(-1),
                Config = ReadConfig(value["Config"]),
            };
        }

        private static void WriteView(JsonWriter writer, PlayerView view)
        {
            if (view == null) { writer.Null(); return; }

            writer.BeginObject();
            writer.Field("Viewer", view.Viewer);
            writer.Name("Config");
            WriteConfig(writer, view.Config);
            writer.Field("Phase", (int)view.Phase);
            writer.Field("RoundNumber", view.RoundNumber);
            writer.Field("CurrentPlayer", view.CurrentPlayer);
            writer.Field("RoundStarter", view.RoundStarter);
            writer.Field("FirstPassComplete", view.FirstPassComplete);
            writer.Field("HighestBid", view.HighestBid);
            writer.Field("HighestBidder", view.HighestBidder);
            writer.Field("Challenger", view.Challenger);
            writer.Field("FlipValue", view.FlipValue);
            writer.Field("FlipsMade", view.FlipsMade);
            writer.Field("OwnStackCleared", view.OwnStackCleared);
            writer.Field("LastSkullOwner", view.LastSkullOwner);
            writer.Field("LastChallengeSucceeded", view.LastChallengeSucceeded);
            writer.Field("Winner", view.Winner);
            writer.Field("TotalOnTable", view.TotalOnTable);

            writer.Name("Players").BeginArray();
            if (view.Players != null)
            {
                for (int i = 0; i < view.Players.Length; i++)
                {
                    var player = view.Players[i];
                    writer.BeginObject();
                    writer.Field("Index", player.Index);
                    writer.Field("Name", player.Name);
                    writer.Field("HandCount", player.HandCount);
                    writer.Field("StackCount", player.StackCount);
                    writer.Field("FlippedCount", player.FlippedCount);
                    writer.IntArray("RevealedFromTop", player.RevealedFromTop);
                    writer.Field("Points", player.Points);
                    writer.Field("Eliminated", player.Eliminated);
                    writer.Field("HasPassed", player.HasPassed);
                    writer.Field("CurrentBid", player.CurrentBid);
                    writer.Field("CardsOwned", player.CardsOwned);
                    writer.EndObject();
                }
            }
            writer.EndArray();

            writer.IntArray("MyHand", view.MyHand);
            writer.IntArray("MyStack", view.MyStack);

            writer.Name("LegalActions").BeginArray();
            if (view.LegalActions != null)
            {
                for (int i = 0; i < view.LegalActions.Length; i++) WriteAction(writer, view.LegalActions[i]);
            }
            writer.EndArray();

            writer.EndObject();
        }

        private static PlayerView ReadView(JsonValue value)
        {
            if (value.IsNull) return null;

            var playersValue = value["Players"];
            var players = new PlayerPublicView[playersValue.Type == JsonValue.Kind.Array ? playersValue.Count : 0];
            for (int i = 0; i < players.Length; i++)
            {
                var player = playersValue[i];
                players[i] = new PlayerPublicView
                {
                    Index = player["Index"].AsInt(),
                    Name = player["Name"].AsString(),
                    HandCount = player["HandCount"].AsInt(),
                    StackCount = player["StackCount"].AsInt(),
                    FlippedCount = player["FlippedCount"].AsInt(),
                    RevealedFromTop = player["RevealedFromTop"].AsIntArray(),
                    Points = player["Points"].AsInt(),
                    Eliminated = player["Eliminated"].AsBool(),
                    HasPassed = player["HasPassed"].AsBool(),
                    CurrentBid = player["CurrentBid"].AsInt(),
                    CardsOwned = player["CardsOwned"].AsInt(),
                };
            }

            var actionsValue = value["LegalActions"];
            var actions = new GameAction[actionsValue.Type == JsonValue.Kind.Array ? actionsValue.Count : 0];
            for (int i = 0; i < actions.Length; i++) actions[i] = ReadAction(actionsValue[i]);

            return new PlayerView
            {
                Viewer = value["Viewer"].AsInt(-1),
                Config = ReadConfig(value["Config"]),
                Phase = (Phase)value["Phase"].AsInt(),
                RoundNumber = value["RoundNumber"].AsInt(),
                CurrentPlayer = value["CurrentPlayer"].AsInt(-1),
                RoundStarter = value["RoundStarter"].AsInt(-1),
                FirstPassComplete = value["FirstPassComplete"].AsBool(),
                HighestBid = value["HighestBid"].AsInt(),
                HighestBidder = value["HighestBidder"].AsInt(-1),
                Challenger = value["Challenger"].AsInt(-1),
                FlipValue = value["FlipValue"].AsInt(),
                FlipsMade = value["FlipsMade"].AsInt(),
                OwnStackCleared = value["OwnStackCleared"].AsBool(),
                LastSkullOwner = value["LastSkullOwner"].AsInt(-1),
                LastChallengeSucceeded = value["LastChallengeSucceeded"].AsBool(),
                Winner = value["Winner"].AsInt(-1),
                TotalOnTable = value["TotalOnTable"].AsInt(),
                Players = players,
                MyHand = value["MyHand"].AsIntArray(),
                MyStack = value["MyStack"].AsIntArray(),
                LegalActions = actions,
            };
        }
    }
}
