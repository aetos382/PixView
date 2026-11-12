namespace PixView.Core.Books;

/// <summary>
/// 1 冊の本の情報。
/// </summary>
/// <remarks>
/// 本の元になるファイルは、アプリでは動かさない。ここで持つのは、その場所と、本の情報だけ。
/// </remarks>
public sealed record Book
{
    /// <summary>
    /// 本の識別子。別の端末で作った本とも重ならない。
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// 題名。
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 作者。設定していなければ <see langword="null"/>。
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// 元になるファイルの場所。
    /// </summary>
    /// <remarks>
    /// OS によっては、ファイル システムのパスではなく、URI になる。
    /// </remarks>
    public required string SourceLocation { get; init; }

    /// <summary>
    /// 元になるファイルの種類。
    /// </summary>
    public required BookSourceKind SourceKind { get; init; }

    /// <summary>
    /// 追加した日時。
    /// </summary>
    /// <remarks>
    /// 保管庫は、UTC のミリ秒単位で保存する。読み出した値は、UTC（オフセット 0）で表す。
    /// </remarks>
    public required DateTimeOffset AddedAt { get; init; }

    /// <summary>
    /// 最後に読んだ日時。まだ読んでいなければ <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// 保管庫は、UTC のミリ秒単位で保存する。ミリ秒より細かい部分は切り捨てる。
    /// 読み出した値は、UTC（オフセット 0）で表す。保存したときのオフセットは残らないが、指す時刻は変わらない。
    /// </remarks>
    public DateTimeOffset? LastReadAt { get; init; }
}
