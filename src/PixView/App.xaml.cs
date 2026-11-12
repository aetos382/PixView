using System.Diagnostics.CodeAnalysis;

namespace PixView;

[SuppressMessage(
    "Naming",
    "CA1724:Type names should not match namespaces",
    Justification = "App は .NET MAUI のアプリケーション クラスの慣例の名前。Android の名前空間 Android.App と重なるが、変更しない。")]
internal sealed partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }
}
