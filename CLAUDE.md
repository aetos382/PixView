# PixView

画像を「本」単位で整理して読む画像ビューア。概要、使用する技術、最初のリリースの範囲は README.md を参照。

## 現在の段階

- 骨組み（ソリューション構成、CI、ライセンス）と、本の情報の保存（#2）がある。画面はまだなく、アプリは「PixView」と表示するだけ。
- 最初のリリースの機能は、issue #2〜#16 に分割してある。#2〜#6 には仕様があり、#7 以降は着手が近づいた時点で仕様を書く。
- #6 まで進むと、本を追加して読むところまで一通り使える。その時点で、以降の優先順位を見直す。
- 技術選定に使った試作は `prototype/maui` ブランチにある。捨てる前提のもので、本番コードに流用しない。
- 未確認：手で操作したときのスクロールの見え方、Windows でのキー操作とホイール操作。
- 未解決：試作で、Windows の全画面表示の自動確認が 3 回中 1 回失敗した（原因未特定）。

## 構成

- `src/PixView` - .NET MAUI のアプリ（Android と Windows）。画面と OS 固有の処理を置く。
- `src/PixView.Core` - UI に依存しないロジック（net11.0）。自動テストの対象にする。
- `tests/PixView.Core.Tests` - `PixView.Core` のテスト（MSTest.Sdk、Microsoft.Testing.Platform）。
- NativeAOT と相性の悪いコードを、ビルド時に検出している。`src/PixView.Core` では NativeAOT とトリミングの両方を（`IsAotCompatible`）、`src/PixView` では AOT だけを解析する。NativeAOT で配布するかどうかは未定（#25）。
- パッケージのバージョンは `Directory.Packages.props` で一元管理する。プロジェクト ファイルにはバージョンを書かない。
- パッケージの取得元は nuget.org だけに限定している（`NuGet.config`）。
- パッケージのロック ファイル（`RestorePackagesWithLockFile`）は導入しない。`src/PixView` は OS によって対象フレームワークが変わるため、別の OS で復元するだけでロック ファイルに差分が出る。その状態は許容しない方針。
- ビルドの成果物は、プロジェクトの下ではなく、ルートの `artifacts/` にまとめて出力される。
- テキスト ファイルの改行コードは LF に統一している（`.gitattributes`、`.editorconfig`）。

## コマンド

リポジトリのルートで実行する。

- `dotnet build` - ソリューション全体をビルドする。解析レベルは最大（`latest-all`）で、警告はエラーとして扱う
- `dotnet test -f net11.0` - すべてのテストを実行する。`-f net11.0` を省くと、MAUI アプリまで実行対象になり、デバイスの指定を求められて失敗する
- `dotnet format PixView.slnx` - `.editorconfig` に合わせて整形する。CI は `--verify-no-changes` で確認する
- `dotnet build src/PixView -c Release -f net11.0-windows10.0.19041.0` - Windows 版をビルドする。exe は `artifacts/bin/PixView/release_net11.0-windows10.0.19041.0/PixView.exe`
- `dotnet build src/PixView -c Release -f net11.0-android -t:Install` - Android 版を、接続中の端末またはエミュレーターにインストールする
- `adb shell monkey -p io.github.aetos382.pixview -c android.intent.category.LAUNCHER 1` - Android 版を起動する

## 進め方

- 仕様は機能単位で GitHub の issue に書く（振る舞い、自動テストで確認する受け入れ条件、実機で確認する項目、やらないこと）。
- 仕様や計画の文書ファイルをリポジトリに作らない。残すのは README.md、テスト、CLAUDE.md のみ。
- CLAUDE.md には、ほかのファイルに書いてあることを繰り返すだけの記述をしない。書いておかないと将来間違える懸念があることだけを書く。
- issue 単位で「実装計画 → 実装とテスト → レビュー → マージ」を回す。
- 1 つの issue は、1 つのセッションで扱う。実装計画は、issue にコメントとして残す。

### 工程ごとに使うもの

- 実装計画：小さな issue は直接書く。作り方が分かれる issue では、feature-dev プラグインの `code-architect` エージェントで設計案を比べる。`/feature-dev` の一式は、多数のサブエージェントを起動するので、小さな issue には使わない。
- テスト：dotnet-test プラグインの `writing-mstest-tests` スキルに従って書く。受け入れ条件の各項目を、1 つ以上のテストにする。
- 受け入れ条件の照合：実装の文脈を持たないサブエージェントに、issue と差分だけを渡し、受け入れ条件を 1 項目ずつ判定させる。
- コード レビュー：pr-review-toolkit プラグインの `/pr-review-toolkit:review-pr`、または `/code-review` を使う。
- コミットとプル リクエスト：commit-commands プラグインを使う。

## リポジトリの運用

- main はルールセットで保護されている。変更は、プル リクエスト経由でのみ行える。管理者でも迂回できない。
- マージの方法は、マージ コミットだけ。
- コミットには署名が必須。署名できないときは、署名を省かずに、できる状態になるのを待つ。
- CI の成功は、マージの必須条件にしていない。マージの前に、CI の結果を確認する。必須条件にする場合は、CI の起動条件（文書だけの変更では起動しない方式）を、ジョブ単位で省く方式に作り直す必要がある。
- 依存関係の自動送信（GitHub の設定）は、無効にしている。Linux 上で MAUI のプロジェクトを復元できず、失敗するため。自前のワークフローで送信する案は、#23 で検討する。
- CodeQL による解析（C# とワークフロー）を、有効にしている。

### Dependabot

Dependabot のプル リクエストを扱うときは、次の点に注意する。

- 更新でも、ライセンスを確認する。版の更新で、依存するパッケージが増えたり、ライセンスが変わったりすることがある。その変化は、プル リクエストの差分には現れない。
- .NET SDK の更新を取り込むときは、手元の SDK も同じ版に更新する。
- Dependabot が作るコミットとプル リクエストの題名は、英語のままでよい。
- MAUI のバージョンは、プロパティ（`MauiVersion`）を経由して指定している。Dependabot がこれを正しく更新できるかは、まだ確かめていない。最初の更新のプル リクエストで、`Directory.Packages.props` の差分を確認する。

## ライブラリのライセンス

NuGet などのパッケージを参照として追加するときは、ライセンスを次の基準で判断する。

- 許可：MIT、BSD（2 条項、3 条項）、BSD-2-Clause-Patent、Apache-2.0、ISC、Zlib、パブリック ドメイン相当。
- 禁止：GPL、LGPL、AGPL、非商用に限定するもの。
- 相談：上のどちらにも当てはまらないもの。追加する前にユーザーに確認する。
- 複数のライセンスから選べるパッケージは、選択肢に許可のものが含まれていれば、許可とする。
- 許可のライセンスであっても、バイナリ パッケージの利用に別の条件が付いているもの（Open Source Maintenance Fee など）は、相談とする。

他のプロジェクトのソース コードを取り込むときは、許可の一覧を適用しない。禁止のライセンスのものは取り込まず、それ以外はすべて相談とする。

ライブラリを選ぶときは、ライセンスに加えて、NativeAOT と相性がよいことを条件にする。リフレクションや、実行時のコード生成に頼るものは避ける。相性の悪いものしか選べない場合は、ユーザーに確認する。

ライセンスは、パッケージを追加する前に調べる。対象は、そのパッケージと、それが依存するパッケージ。禁止や相談に当たるものがあれば、追加せずにユーザーに確認する。調べた結果は、実装計画に書き、プル リクエストの説明にも載せる。

## 言語

- リポジトリ内の文書、コードのコメント、コミット メッセージは日本語で書く。

## 画面の確認

- 画面に関わる変更や計測は、画面を画像に保存して目で確認してから完了とする。数値やログだけで判断しない。
- `./scripts/Save-AppScreenshot.ps1 -Platform Windows -ProcessName <プロセス名> -OutFile <ファイル>` - Windows のアプリのウィンドウを保存する
- `./scripts/Save-AppScreenshot.ps1 -Platform Android -OutFile <ファイル>` - Android の画面を保存する
- PC がロックされている間は、Windows のウィンドウは真っ白にしか取得できない（スクリプトはエラーにする）。その場合は UI オートメーションで画面上の要素を列挙して内容を確かめ、画像での確認が残っていることを報告する。
- 静止画では動きの滑らかさを判断できない。スクロール中の見え方などは、人による確認が必要な項目として報告する。

## 注意点

- MAUI は 11.0.10 以降を使う（`Directory.Packages.props` の `MauiVersion`）。SDK 付属の 11.0.0 は、Android の格子表示が項目数に比例して遅くなる（dotnet/maui#38925）。
- Windows の CollectionView で GridItemsLayout を使うときは、項目に幅を明示し、`ItemSizingStrategy` を `MeasureAllItems` にする。画像を含む項目で幅を省くか、`MeasureFirstItem` を使うと 1 列になる（MAUI 11.0.10 で確認。dotnet/maui に該当する issue は見つかっていない）。
- MAUI の `OnAppearing` では `Page.Window` が null のことがある。ウィンドウに依存する処理は `Loaded` で行う。
- Microsoft.Data.Sqlite の非同期メソッド（`OpenAsync`、`ExecuteReaderAsync` など）は、内部で同期的に動く。公式の勧めに従って呼ばずに、同期メソッドを使う。
- ソリューションにプロジェクトを追加するときは、`dotnet sln add --include-references false` を使う。既定のままだと、参照先のプロジェクトがすでに登録されている場合に失敗し、何も追加されない（SDK 11.0.100）。
- ひな形から生成したファイルは、BOM 付きで改行コードが CRLF のことがある。追加したファイルは、BOM なしの UTF-8、改行コード LF に直す。`dotnet format` が直すのは C# のファイルだけなので、XAML や XML は別に変換する。
- 型は既定で internal にし、派生させない型は sealed にする。XAML に対応するクラスを internal にするときは、XAML のルート要素に `x:ClassModifier="Internal"` を付ける。付けないと、生成される側が public になり、ビルド エラー（CS0262）になる。
- 解析の指摘を抑制するときは、対象の型やメンバーに `SuppressMessage` 属性を付け、`Justification` に理由を書く。ルールがプロジェクト全体に当てはまらない場合や、指摘の対象が自分で直せないコードの場合は、プロジェクト単位（`NoWarn`）で抑制してよい。その場合は、設定する箇所にコメントで理由を書く。
- 構成によって使わなくなる `using` は、`#if` で囲む。未使用の `using` はビルド エラーになる。
- CI は、ビルド、整形、テストの結果に影響しないファイル（Markdown、`LICENSE`、`.gitignore`、`.github/dependabot.yml`、`scripts/` の下）だけの変更では実行されない。無視する一覧は `.github/workflows/ci.yml` にある。ビルドに影響しないファイルの種類が増えたら、一覧に加える。CodeQL のワークフロー（`.github/workflows/codeql.yml`）にも同じ一覧があるので、そろえて変更する。
- CodeQL は、自前のワークフローで動かしている。C# は、ビルドをせずに解析する方式。MAUI に関わる型の情報が一部欠けるが、ワークロードの導入が不要で、短時間で終わる。
- ワークフローで使うアクションを追加・更新したら、`gh actions-lock` を実行して `.github/workflows/actions.lock` を更新する。このファイルは手で編集しない。
