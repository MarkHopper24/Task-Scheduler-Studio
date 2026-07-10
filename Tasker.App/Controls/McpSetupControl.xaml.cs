using Microsoft.UI.Xaml.Controls;
using Tasker_App.ViewModels;

namespace Tasker_App.Controls;

public sealed partial class McpSetupControl : UserControl
{
    public McpSetupViewModel ViewModel { get; } = new();

    public McpSetupControl()
    {
        InitializeComponent();
    }
}
