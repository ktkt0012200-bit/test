# オンライン対戦の構成

## 1. 最重要の判断: リレー型は使えない

Unity のオンライン対戦は、Netcode for GameObjects + Relay や Photon PUN のような
**「プレイヤーの 1 人がホストになる」方式**が定番で、サーバ運用が不要なので安く速い。

**本作ではこれを採用できない。**

スカル系のゲームは「伏せられたカードが誰にも見えない」ことだけで成立している。
ホストがゲームロジックを持つ構成では、**ホストのメモリに全員の伏せカードが載る**。
メモリ改変ツールで覗けば、そのプレイヤーは常に勝てる。難読化や暗号化で軽減はできても、
クライアントが正解データを持っている限り原理的に防げない。

したがって:

- **権威サーバ**が `GameState` を保持する唯一の場所
- クライアントには `ViewRedactor` を通した `PlayerView`(その席から見えていい情報だけ)しか送らない
- クライアントから届いたアクションは `GameEngine.IsLegal` で照合してから適用する。
  クライアントを信用する箇所はひとつも無い

サーバ 1 台の運用コストは、この手のターン制ゲームでは無視できるほど小さい
(状態は数 KB、通信は 1 手ごとに数往復だけ)。ブラフゲームでは払う価値のあるコスト。

## 2. 全体像

```
              ┌────────────────────────────────────────────┐
              │  SkullDive.Server  (ASP.NET Core / net8.0) │
              │                                            │
              │   RoomRegistry ── Room ── GameState  ←── 権威ある盤面はここだけ
              │                     │                      │
              │                     │ ViewRedactor         │
              │                     ▼                      │
              │            PlayerView (席ごとに別内容)      │
              └──────────────┬──────────────┬──────────────┘
                    WebSocket│              │WebSocket
                             ▼              ▼
                    ┌────────────────┐  ┌────────────────┐
                    │ Unity クライアント│  │ Unity クライアント│
                    │  GameClient     │  │  GameClient     │
                    │  (ルール実行なし) │  │  (ルール実行なし) │
                    └────────────────┘  └────────────────┘
```

`GameEngine` / `Odds` / `AiBrain` のソースは 1 か所
(`unity/SkullDive/Assets/SkullDive/{Core,Ai,Net}`)にしかなく、
Unity とサーバの両方が同じファイルをコンパイルしている。コピーは存在しない。

## 3. プロトコル

WebSocket + JSON。`Protocol.Version` で世代管理する。

### クライアント → サーバ

| 種別 | 内容 |
|---|---|
| `Join` | `RoomCode`(空なら新規作成)、`Name`、`Token`(再接続時) |
| `Start` | ホストのみ。対戦開始 |
| `AddBot` | ホストのみ。AI を 1 席追加 |
| `Action` | `GameAction` を 1 つ |
| `Ping` / `Leave` | 死活確認 / 退出 |

### サーバ → クライアント

| 種別 | 内容 |
|---|---|
| `Welcome` | 割り当てられた席番号と再接続トークン |
| `Room` | 席一覧、開始状態、ルール設定 |
| `State` | **その席から見た** `PlayerView` + 秘匿処理済みイベント列 + 手番の残り時間 |
| `Error` | 人間が読めるエラー文 |
| `Pong` | — |

### シリアライザを共有していない理由

サーバは `System.Text.Json`、Unity は `Newtonsoft.Json`(UPM の `com.unity.nuget.newtonsoft-json`)を使う。
netstandard2.1 の共有ライブラリに両方を持ち込めないため、DTO だけを共有し、
JSON の実装は `IMessageCodec` の背後に置いて差し替えている。

両者が一致する条件は 3 つだけで、これはテストで固定してある:

1. DTO は **public フィールド**で構成する
2. `System.Text.Json` 側は **`IncludeFields = true` が必須**(忘れると中身が空のメッセージを送る)
3. **enum は数値**で書き出す(どちらの既定も数値。文字列化すると噛み合わなくなる)

## 4. 進行の制御

`Room` が 1 卓分の状態機械を持ち、`RoomRegistry` が 250 ms ごとに全卓を tick する。

- **手番の制限時間** (既定 20 秒): 切れたら `AiBrain` がその席の手を打つ。
  放置や切断でゲームが止まらないことを最優先にしている。ターン制ゲームの離脱要因として最も多い
- **ラウンド決着の待ち** (既定 3.5 秒): 全員がリビール演出を見終える時間を確保してから次ラウンドへ進む。
  ここを省くと、演出中のプレイヤーだけが手番を飛ばされる
- **bot 席**: 人数が足りないときにホストが追加できる。時間切れ代打と同じコードを使う
- **再接続**: `Welcome` で渡したトークンで元の席に戻れる。開始後の席は切断しても保持される

サーバ側の AI も `ViewRedactor` を通した `PlayerView` しか受け取らない。
つまり**サーバの AI もカンニングしない**。人間と同じ情報で判断する。

いずれも `SkullDive__TurnTimeoutMs` / `SkullDive__RoundEndDelayMs`(環境変数または appsettings.json)で
再ビルドなしに変えられる。

## 5. 動かし方と検証

```bash
# サーバ
dotnet run --project dotnet/SkullDive.Server        # http://0.0.0.0:5099
curl http://127.0.0.1:5099/healthz

# 結合テスト: 2 クライアント + bot 2 で 1 マッチ完走させ、秘匿情報の漏れも検査する
dotnet run --project dotnet/SkullDive.Cli -- --net-smoke

# guest を放置し、サーバの時間切れ代打が働くことを確認する
SkullDive__TurnTimeoutMs=1500 dotnet run --project dotnet/SkullDive.Server &
dotnet run --project dotnet/SkullDive.Cli -- --net-smoke --idle-guest

# まとめて
./scripts/check.sh
```

`--net-smoke` はワイヤー越しに届いた `PlayerView` を毎回検査している:

- View の宛先(`Viewer`)が自分の席と一致する
- 自分の山の枚数が公開情報と食い違わない
- どの席についても「公開済みカード数 == めくった枚数」— つまり未公開カードが 1 枚も含まれていない
- `CardPlaced` / `CardDiscarded` イベントのカード情報が、自分以外の席では伏せられている

## 6. Unity 側

`GameClient`(共有コード)が WebSocket と JSON を扱い、受信メッセージをキューに積む。
Unity ではメインスレッド以外から UI を触れないため、`OnlineGameController.Update` が
`TryDequeue` で拾って反映する。

**クライアントではルールを一切実行しない。** ローカル先読み(予測)を入れたくなるが、
伏せカードを知らないクライアントではそもそも結果を計算できないため、必ずズレて破綻する。
`OnlineGameController` は View を表示し操作を送るだけの薄い層に留めてある。

### WebGL について

`System.Net.WebSockets.ClientWebSocket` は WebGL では動かない(ブラウザの制約)。
WebGL でも配信する場合は JavaScript の `WebSocket` を叩く `IMessageCodec` 相当の
トランスポート実装を別途用意する必要がある。iOS / Android のネイティブビルドはそのまま動く。

## 7. 本番運用で足りていないもの

現状は「仕組みとして正しく動く」段階。商用配信の前に必要なもの:

- **TLS (wss://)** とリバースプロキシ。現状は平文 ws
- **認証**。今は名前を自己申告するだけ。アカウントと紐付ける必要がある
- **レート制限 / 入室制限**。1 IP からのルーム大量生成を止める仕組みが無い
- **水平スケール**。今は 1 プロセスのメモリにルームを持つので、複数台に増やすなら
  ルームをノードに固定する仕組み(sticky routing)か外部ストアが必要
- **観戦とマッチメイキング**。現状はルームコードを直接共有する方式のみ
- **メトリクス / 構造化ログ**
