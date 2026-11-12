using System.Diagnostics.CodeAnalysis;

namespace PixView.WinUI;

/// <summary>
/// Windows 版のアプリケーション クラス。
/// </summary>
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "WinUI の XAML コンパイラーが生成する XamlTypeInfo.g.cs が、このクラスを public partial として宣言するため、internal にできない。")]
public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
