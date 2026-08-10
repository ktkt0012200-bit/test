using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SkullDive.Core;
using SkullDive.Net;

namespace SkullDive.Server
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSingleton(ServerOptions.FromConfiguration(builder.Configuration));
            builder.Services.AddSingleton<RoomRegistry>();

            var app = builder.Build();
            var registry = app.Services.GetRequiredService<RoomRegistry>();

            var lifetime = app.Lifetime;
            var pump = registry.RunPumpAsync(lifetime.ApplicationStopping);

            app.UseWebSockets();

            app.MapGet("/healthz", () => Results.Json(new
            {
                status = "ok",
                protocol = Protocol.Version,
                rooms = registry.Count,
            }));

            app.Map("/ws", async (HttpContext context) =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("websocket required");
                    return;
                }

                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                await RunConnectionAsync(socket, registry, lifetime.ApplicationStopping);
            });

            await app.RunAsync();
            await pump;
        }

        /// <summary>1 接続の一生。最初のメッセージは必ず Join。</summary>
        private static async Task RunConnectionAsync(WebSocket socket, RoomRegistry registry,
            CancellationToken cancellation)
        {
            var buffer = new byte[64 * 1024];
            Room room = null;
            Seat seat = null;

            try
            {
                while (socket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
                {
                    string json = await ReadMessageAsync(socket, buffer, cancellation).ConfigureAwait(false);
                    if (json == null) break;

                    ClientMessage message;
                    try
                    {
                        message = WireCodec.DecodeClient(json);
                    }
                    catch (Exception)
                    {
                        await SendRawErrorAsync(socket, "メッセージを解釈できませんでした").ConfigureAwait(false);
                        continue;
                    }
                    if (message == null) continue;

                    if (seat == null)
                    {
                        if (message.Kind != ClientMessageType.Join)
                        {
                            await SendRawErrorAsync(socket, "最初に join してください").ConfigureAwait(false);
                            continue;
                        }

                        // コード未指定なら新規ルームを作る。指定があれば既存ルームに入る。
                        room = string.IsNullOrEmpty(message.RoomCode)
                            ? registry.Create(GameConfig.Default())
                            : registry.Find(message.RoomCode);

                        if (room == null)
                        {
                            await SendRawErrorAsync(socket, "そのルームは見つかりません").ConfigureAwait(false);
                            continue;
                        }

                        seat = await room.JoinAsync(message.Name, message.Token, socket).ConfigureAwait(false);
                        if (seat == null)
                        {
                            await SendRawErrorAsync(socket, "満席、または開始済みのため参加できません").ConfigureAwait(false);
                            room = null;
                            continue;
                        }
                        continue;
                    }

                    if (message.Kind == ClientMessageType.Leave) break;
                    await room.HandleAsync(seat, message).ConfigureAwait(false);
                }
            }
            catch (WebSocketException)
            {
                // 予期しない切断は通常運転として扱う。
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (room != null && seat != null)
                {
                    await room.DisconnectAsync(seat).ConfigureAwait(false);
                }
            }
        }

        /// <summary>1 メッセージ分を読み切る。接続が閉じたら null。</summary>
        private static async Task<string> ReadMessageAsync(WebSocket socket, byte[] buffer,
            CancellationToken cancellation)
        {
            var builder = new StringBuilder();

            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation)
                    .ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close) return null;

                builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage) return builder.ToString();
            }
        }

        private static async Task SendRawErrorAsync(WebSocket socket, string error)
        {
            if (socket.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(WireCodec.EncodeServer(new ServerMessage
            {
                Kind = ServerMessageType.Error,
                Error = error,
            }));
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                CancellationToken.None).ConfigureAwait(false);
        }
    }
}
