using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Compass.Plugins;
using Compass.Services;
using Compass.Services.Interfaces;
using Compass.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Compass
{
    /// <summary>
    /// Main window for Compass - a Windows spotlight-style search assistant.
    ///
    /// This partial class is split across multiple files for better organization:
    /// - MainWindow.Core.cs: Fields, constructor, lifecycle, hotkeys, tray
    /// - MainWindow.Search.cs: Search, input handling, app launching
    /// - MainWindow.Chat.cs: Chat UI, AI integration, images
    /// - MainWindow.Manager.cs: Settings UI, shortcuts, commands
    /// - MainWindow.Personalization.cs: Theming and appearance
    /// - MainWindow.Widgets.cs: Widget system and rendering
    /// - MainWindow.Utilities.cs: Helper methods
    /// </summary>
    public partial class MainWindow : Window
    {
        // All implementation is in partial class files
        // This file exists for WPF designer compatibility
    }
}
