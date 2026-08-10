# Unity で動かす手順

Unity Editor で実際に触るための手順。前準備はリポジトリ側で済ませてあるので、
やることは「開く → メニューを 3 回押す → 再生」だけ。

## 必要なもの

- **Unity 6 LTS (6000.0.x)**。`unity/SkullDive/ProjectSettings/ProjectVersion.txt` が
  `6000.0.0f1` を指しているので、別のバージョンを使う場合は Unity Hub の指示に従って
  アップグレードするか、このファイルを書き換える
- 追加パッケージのインストールは不要。**外部パッケージに一切依存していない**
  (JSON も自前実装。`Packages/manifest.json` に並んでいるのは Unity 同梱の組み込みモジュールだけ)
- オンライン対戦を試す場合のみ .NET SDK 8(サーバを動かすため)

## 手順

### 1. プロジェクトを開く

Unity Hub → Add → `unity/SkullDive` を選ぶ → 開く。

初回は Library フォルダの生成に数分かかる。`.meta` ファイルはこのときに作られる
(リポジトリには入れていない。asmdef の参照は GUID ではなく**アセンブリ名**で書いてあるので、
`.meta` が無い状態から開いても参照が壊れない)。

### 2. メニューを上から順に実行する

メニューバーの **Tools > SkullDive** に 3 つ並んでいる。

| メニュー | 何をするか |
|---|---|
| **1. 動作確認シーンを作成** | `Assets/SkullDive/Scenes/Harness.unity` を生成して開く |
| **2. エンジンの自己診断を実行** | ソロ 1 マッチ完走 + 200 マッチのシミュレーション + 秘匿処理の確認を Console に出す |
| **3. モバイル向け設定を適用** | 画面の向きを縦固定にし、製品名を設定する |

**まず「2. エンジンの自己診断」を実行するのがおすすめ。**
これが通れば、共有エンジン・AI・秘匿処理が Unity 上で正しく動いていることが確定するので、
以降の不具合を UI 層だけに絞り込める。Console にこう出れば OK:

```
[SkullDive] ソロ 1 マッチ完走 (ラウンド 4 / 勝者 席1)
[SkullDive] 200 マッチのシミュレーション: 成功率 72.8% / 平均宣言 3.94 / ラウンド per match 4.1
[SkullDive] 自己診断 OK — エンジン・AI・秘匿処理はすべて Unity 上で動作しています
```

### 3. 再生する

再生ボタンを押すと「ソロ」タブでゲームが始まる。ボタンを押して進める。

- 相手 3 人の**癖(テル)**が各行に出る。これを覚えると勝てるようになる
- チャレンジ中は各山の**安全度が %** で出る(製品版ではここをゲージ表示にする)
- 山の表記: `(?)` は未公開、`(R)` は自分だけ見えているカード、`[R]` は全員に公開されたカード

### 4. オンライン対戦を試す

別ターミナルでサーバを起動する。

```bash
dotnet run --project dotnet/SkullDive.Server
```

`http://127.0.0.1:5099/healthz` が `{"status":"ok",...}` を返せば起動している。

Unity 側で「オンライン」タブ →

1. ルームコードは空のまま **接続**(空だと新規ルームが作られる)
2. **bot を追加** を 2〜3 回押す
3. **開始**

2 人で対戦する場合は、1 人目が表示されたルームコードを相手に伝え、
相手はそのコードを入れて接続する。同じ PC で試すなら Unity の再生と
`dotnet run --project dotnet/SkullDive.Cli -- --net-smoke` を併用してもよい。

## この画面は製品 UI ではない

`HarnessScreen` は IMGUI(Unity の開発用即時 GUI)で描いた**動作確認用の画面**。
Prefab も Canvas も要らず、スクリプトを 1 個置くだけで動くことを優先している。

製品 UI(縦持ち片手操作、スワイプでめくる演出、読みメーター、AI のテルを顔の微アニメで出す)は
[game-design.md](game-design.md) の設計に沿って次に作る。

## うまくいかないとき

**コンパイルエラーが出る**

`Game/` と `Editor/` は Unity Editor が無い環境で書いたコードなので、可能性はある。
リポジトリ側では以下まで検証済み:

- `Game/`(`SoloGameController` / `OnlineGameController` / `HarnessScreen`)は
  **Unity 公式の参照アセンブリ (NuGet の `UnityEngine.Modules`) に対してコンパイルが通る**ことを確認済み。
  実際の UnityEngine API シグネチャに対する本物の検証
- `Editor/`(`HarnessSceneBuilder`)は UnityEditor の参照アセンブリが公開されていないため、
  自前で書いたスタブ宣言に対する検証。**Unity 本体とのシグネチャ一致は保証されない**ので、
  エラーが出るとしたらここが最有力

いずれも `dotnet build dotnet/SkullDive.UnityCheck` で再現できる。
エラー文を貼ってもらえれば直せる。

**`Tools > SkullDive` メニューが出ない**

`Editor/` のコンパイルが通っていない。Console のエラーを確認する。

**オンラインタブで「接続失敗」**

- サーバが起動しているか(`curl http://127.0.0.1:5099/healthz`)
- URL が `ws://127.0.0.1:5099/ws` になっているか(Inspector の `OnlineGameController` で変更可)
- **WebGL ビルドでは動かない。** `ClientWebSocket` がブラウザで使えないため。
  iOS / Android のネイティブビルドと Editor では動く

**実機ビルドで挙動が変わる**

JSON をリフレクションではなく手書きで読み書きしているので、
IL2CPP のマネージドコード除去でフィールドが消える類の問題は起きない設計にしてある。
それでも差異が出た場合は Player Settings の Managed Stripping Level を下げて切り分ける。
