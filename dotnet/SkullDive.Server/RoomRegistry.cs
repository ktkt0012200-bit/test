using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkullDive.Core;

namespace SkullDive.Server
{
    /// <summary>ルームコードからルームを引く。あわせて全ルームの tick と掃除も回す。</summary>
    public sealed class RoomRegistry
    {
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 紛らわしい 0/O/1/I を除く
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

        private readonly ConcurrentDictionary<string, Room> _rooms = new ConcurrentDictionary<string, Room>();
        private readonly Random _random = new Random();
        private readonly ServerOptions _options;

        public RoomRegistry(ServerOptions options)
        {
            _options = options ?? new ServerOptions();
        }

        public int Count
        {
            get { return _rooms.Count; }
        }

        public Room Find(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            Room room;
            return _rooms.TryGetValue(code.ToUpperInvariant(), out room) ? room : null;
        }

        public Room Create(GameConfig config)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                string code = NewCode();
                var room = new Room(code, config, Environment.TickCount ^ code.GetHashCode(),
                    _options.TurnTimeoutMs, _options.RoundEndDelayMs);
                if (_rooms.TryAdd(code, room)) return room;
            }
            throw new InvalidOperationException("could not allocate a room code");
        }

        private string NewCode()
        {
            var chars = new char[4];
            lock (_random)
            {
                for (int i = 0; i < chars.Length; i++) chars[i] = CodeAlphabet[_random.Next(CodeAlphabet.Length)];
            }
            return new string(chars);
        }

        /// <summary>
        /// 全ルームの時間切れ処理を回し、空になったルームを片付ける。
        /// これが止まると放置された卓が永久に進まなくなるので、必ず起動時に走らせる。
        /// </summary>
        public async Task RunPumpAsync(CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(250, cancellation).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                var stale = new List<string>();
                foreach (var pair in _rooms)
                {
                    var room = pair.Value;
                    try
                    {
                        await room.TickAsync().ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // 1 ルームの失敗で他の卓を止めない。
                    }

                    if (room.IsEmpty && DateTime.UtcNow - room.LastActivityUtc > IdleTimeout)
                    {
                        stale.Add(pair.Key);
                    }
                }

                foreach (var code in stale)
                {
                    Room removed;
                    _rooms.TryRemove(code, out removed);
                }
            }
        }
    }
}
