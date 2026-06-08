using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Tasker_App;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1240 * scale), (int)(820 * scale)));

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));

        // Apply the saved backdrop + theme to this window.
        Tasker_App.Services.ThemeManager.ApplyTo(this);
    }

    /// <summary>The shell page hosted in the root frame (owns the window pin/always-on-top state).</summary>
    public MainPage? HostedPage => RootFrame.Content as MainPage;

    /// <summary>Hides the app title bar and caption buttons for Quick Launch's widget mode. The
    /// collapsed title bar leaves no system drag region, so the page drives window movement manually
    /// (see QuickLaunchPage) to let any empty surface drag the window.</summary>
    public void EnterWidgetChrome()
    {
        AppTitleBar.Visibility = Visibility.Collapsed;
        if (AppWindow.Presenter is OverlappedPresenter p)
            p.SetBorderAndTitleBar(true, false); // drop the min/max/close caption buttons
    }

    /// <summary>Restores the normal title bar and caption buttons when leaving widget mode.</summary>
    public void ExitWidgetChrome()
    {
        if (AppWindow.Presenter is OverlappedPresenter p)
            p.SetBorderAndTitleBar(true, true);
        SetTitleBar(AppTitleBar);
        AppTitleBar.Visibility = Visibility.Visible;
    }
}
