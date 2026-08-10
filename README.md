# SKULL DIVE

ボードゲーム『スカル』(Skull & Roses) をベースにしたスマホ向けカジュアルゲーム。
オリジナル要素は **「クラウン」カード 1 枚**だけ — めくられると 2 枚分としてカウントされる。

- ゲーム設計とバランス実測値: [docs/game-design.md](docs/game-design.md)
- オンライン対戦の構成: [docs/online-architecture.md](docs/online-architecture.md)
- **Unity で動かす手順**: [docs/unity-setup.md](docs/unity-setup.md)

## すぐ試す

Unity は不要。ターミナルで遊べる。

```bash
# ソロプレイ (AI 3 人が相手)
dotnet run --project dotnet/SkullDive.Cli

# バランス測定 — AI 同士で 5000 マッチ回して統計を出す
dotnet run --project dotnet/SkullDive.Cli -- --sim 5000

# 原作ルールとクラウン入りを並べて比較する
dotnet run --project dotnet/SkullDive.Cli -- --compare 5000

# テスト一式 + サーバ結合テスト
./scripts/check.sh
```

必要なもの: .NET SDK 8。

## 構成

```
unity/SkullDive/Assets/SkullDive/
  Core/     ルールエンジン。純 C#、UnityEngine 非依存        ← ソースの正本
  Ai/       AI の性格と判断、バランスシミュレータ
  Net/      通信プロトコルとクライアント
  Game/     Unity 側の進行役と動作確認画面 (MonoBehaviour)
  Editor/   エディタ拡張

dotnet/
  SkullDive.Shared/     上記 Core/Ai/Net を「同じファイルのまま」コンパイルする
  SkullDive.Tests/      73 テスト。外部パッケージ非依存のコンソールランナー
  SkullDive.Cli/        ターミナル版ゲーム + バランス測定 + 結合テスト
  SkullDive.Server/     権威サーバ (ASP.NET Core WebSocket)
  SkullDive.UnityCheck/ Unity Editor 無しで Unity 向けコードのコンパイルを検証する
  SkullDive.Json/       System.Text.Json による参照実装 (テストのオラクル専用)
```

### 設計の要点

**1. ルールエンジンは 1 か所にしかない**

`Core/` `Ai/` `Net/` のソースは `unity/` 配下の 1 か所だけに存在する。
`dotnet/SkullDive.Shared` はそのファイルを直接コンパイルしているので、
Unity・サーバ・テスト・CLI がすべて同一のコードを動かす。**コピーは存在しない。**

エンジンは `UnityEngine` に依存しない (asmdef に `noEngineReferences: true` を設定済み)。
そのおかげでサーバ上でも動き、Unity を開かずにテストできる。

**2. 秘匿情報の扱いを型で強制する**

ブラフゲームは「伏せカードが見えない」ことだけで成立している。そこで:

- `GameState`(全員の手札を含む完全な盤面)を持つのはサーバとソロモードのドライバだけ
- 外に出すときは必ず `ViewRedactor` で `PlayerView` に落とす。
  `PlayerView` には他プレイヤーの**枚数と公開済みカードしか入らない**
- イベントも `EventRedactor` を通す(伏せて置いた / 伏せて捨てたカードは本人以外に見えない)
- **AI も `PlayerView` しか受け取らない。** サーバ側の代打 AI も同じ。構造的にカンニングできない

`PlayerPublicView` にフィールドを足すと落ちるテストを置いてある。
秘匿情報を運ぶフィールドを不注意に追加できないようにするため。

**3. オンラインは権威サーバ方式**

ホストがゲームを持つリレー型(NGO + Relay、Photon PUN など)は採用していない。
ホストのメモリに全員の伏せカードが載るため、ブラフゲームでは原理的にチートを防げない。
理由と構成は [docs/online-architecture.md](docs/online-architecture.md) に詳しい。

**4. 外部パッケージにゼロ依存**

通信の JSON はリフレクションを使わない手書き実装 (`Net/WireCodec.cs`) で、サーバと Unity が
同一のコードを使う。理由は 3 つ:

- Unity 側にパッケージを追加させない(セットアップの手間とバージョン依存が消える)
- **IL2CPP のマネージドコード除去でフィールドが消えない。** リフレクション経由でしか
  触られない DTO のフィールドは実機ビルドで削られることがあり、
  「エディタでは動くが実機で空になる」という形で壊れる
- 実装が 1 つなので、シリアライザ間で書式がズレる余地が無い

手書きなので、標準的なシリアライザ (System.Text.Json) を独立したオラクルとして
双方向の相互運用をテストで固定している。

**5. バランスは測って決める**

`--compare` で「原作 / クラウン value=2 / value=3 / 自分の山は 1 枚分」を同条件で比較できる。
クラウンの値を 2 にした根拠は実測値。詳細は [docs/game-design.md](docs/game-design.md)。

## Unity で開く

```
unity/SkullDive/ を Unity Hub から開く
→ Tools > SkullDive > 1. 動作確認シーンを作成
→ Tools > SkullDive > 2. エンジンの自己診断を実行
→ Tools > SkullDive > 3. モバイル向け設定を適用
→ 再生
```

**追加パッケージのインストールは不要**(外部依存ゼロ)。
`ProjectSettings/ProjectVersion.txt` は `6000.0.0f1` (Unity 6 LTS) を指している。
別のバージョンを使う場合はこのファイルを書き換えるか、Unity Hub でバージョンを選び直す。

手順とトラブルシューティングの詳細は [docs/unity-setup.md](docs/unity-setup.md)。

## 検証状況

正直に区別しておく。

| 対象 | 状態 |
|---|---|
| ルールエンジン / 秘匿処理 / AI / 確率計算 / 通信コーデック | **テスト済み** (73 テスト。2〜6 人 × クラウン有無で 1200 マッチの soak を含む) |
| 通信プロトコルの往復 | **テスト済み** |
| サーバ (ルーム / 検証 / bot / 時間切れ代打 / ラウンド送り) | **実サーバに接続する結合テストで確認済み** |
| Unity `Game/` (`SoloGameController` / `OnlineGameController` / `HarnessScreen`) | **コンパイル検証済み。** Unity 公式の参照アセンブリ (NuGet `UnityEngine.Modules`) に対して通る |
| Unity `Editor/` (`HarnessSceneBuilder`) | **自前スタブに対してのみ検証。** UnityEditor の参照アセンブリが公開されていないため、Unity 本体とのシグネチャ一致は保証されない |
| Unity 上での実行 (再生、シーン生成) | **未検証。** この環境に Unity Editor が無いため |

Unity 向けコードのコンパイル検証は `dotnet build dotnet/SkullDive.UnityCheck` で再現できる
(`./scripts/check.sh` にも含まれている)。Unity Editor では
**Tools > SkullDive > 2. エンジンの自己診断を実行** を最初に押すのがおすすめ。
共有エンジン・AI・秘匿処理が Unity 上で動いていることが確定するので、
以降の不具合を UI 層だけに絞り込める。

`HarnessScreen` は IMGUI で書いた**開発用の動作確認画面**で、製品 UI ではない。
プレハブも Canvas も不要で、シーンにスクリプトを 1 個置くだけでソロ / オンラインを通しで遊べる。
実機向けの UI(縦持ち片手操作、スワイプでめくる演出、読みメーター)は
[docs/game-design.md](docs/game-design.md) の設計に沿って次に作る。

## ルール設定

`GameConfig` にまとまっている。バランス調整はここだけを触る。

| 項目 | 既定 | 意味 |
|---|---|---|
| `RoseCount` / `CrownCount` / `SkullCount` | 2 / 1 / 1 | 初期手札。`CrownCount = 0` にすると原作ルールになる |
| `CrownValue` | 2 | クラウンをめくったときのカウント値 |
| `CrownCountsAsOneInOwnStack` | false | true にすると自分の山のクラウンだけ 1 枚分になる(クラウンが強すぎた場合の調整ノブ) |
| `PointsToWin` | 2 | 勝利に必要なポイント |
| `OneCardWinEnabled` | true | 手札 1 枚でチャレンジ成功したら即勝利 |
| `ChooseDiscardOnOwnSkull` | true | 自分のスカルを踏んだとき、失うカードを自分で選べる |
