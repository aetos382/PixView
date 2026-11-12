namespace PixView.Core.Books;

/// <summary>
/// 本を追加した結果。
/// </summary>
/// <param name="Book">
/// 追加した本。同じ場所の本がすでに登録されていた場合は、登録済みの本。
/// </param>
/// <param name="IsAlreadyRegistered">
/// 同じ場所の本がすでに登録されていて、新しい本を作らなかった場合は <see langword="true"/>。
/// </param>
public sealed record AddBookResult(Book Book, bool IsAlreadyRegistered);
