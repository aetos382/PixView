# PixView

画像を「本」単位で整理して読む画像ビューア。概要、使用する技術、最初のリリースの範囲は README.md を参照。

## 現在の段階

- 技術は .NET MAUI と SkiaSharp に確定した。本番コードはまだない。
- 技術選定に使った試作は `prototype/maui` ブランチにある。捨てる前提のもので、本番コードに流用しない。
- MAUI は 11.0.10 以降を使う。SDK 付属の 11.0.0 は、Android の格子表示が冊数に比例して遅くなる（dotnet/maui#38925）。
- 未確認：手で操作したときのスクロールの見え方、Windows でのキー操作とホイール操作。
- 未解決：試作で、Windows の全画面表示の自動確認が 3 回中 1 回失敗した（原因未特定）。

## 進め方

- 仕様は機能単位で GitHub の issue に書く（振る舞い、自動テストで確認する受け入れ条件、実機で確認する項目、やらないこと）。
- 仕様や計画の文書ファイルをリポジトリに作らない。残すのは README.md、テスト、CLAUDE.md のみ。
- issue 単位で「実装計画 → 実装とテスト → レビュー → マージ」を回す。

## 言語

- リポジトリ内の文書、コードのコメント、コミット メッセージは日本語で書く。

## 画面の確認

- 画面に関わる変更や計測は、画面を画像に保存して目で確認してから完了とする。数値やログだけで判断しない。
- `./scripts/Save-AppScreenshot.ps1 -Platform Windows -ProcessName <プロセス名> -OutFile <ファイル>` - Windows のアプリのウィンドウを保存する
- `./scripts/Save-AppScreenshot.ps1 -Platform Android -OutFile <ファイル>` - Android の画面を保存する
- 静止画では動きの滑らかさを判断できない。スクロール中の見え方などは、人による確認が必要な項目として報告する。

## 注意点

- Windows の CollectionView で GridItemsLayout を使うときは、項目に幅を明示し、`ItemSizingStrategy` を `MeasureAllItems` にする。画像を含む項目で幅を省くか、`MeasureFirstItem` を使うと 1 列になる（MAUI 11.0.10 で確認。dotnet/maui に該当する issue は見つかっていない）。
- MAUI の `OnAppearing` では `Page.Window` が null のことがある。ウィンドウに依存する処理は `Loaded` で行う。
