namespace PixView.Core.Books;

/// <summary>
/// 本の元になるファイルの種類。
/// </summary>
/// <remarks>
/// 値は、保存先に数値で記録する。保存済みのデータを読めなくなるので、既存の値は変えない。
/// </remarks>
public enum BookSourceKind
{
    /// <summary>
    /// 画像ファイルが入ったフォルダ。
    /// </summary>
    Folder = 0,

    /// <summary>
    /// ZIP（CBZ を含む）。
    /// </summary>
    Zip = 1,

    /// <summary>
    /// PDF。
    /// </summary>
    Pdf = 2,
}
