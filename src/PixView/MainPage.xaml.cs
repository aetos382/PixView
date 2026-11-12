using System.Diagnostics.CodeAnalysis;

namespace PixView;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "AppShell.xaml の DataTemplate を通じて生成される。")]
internal sealed partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }
}
