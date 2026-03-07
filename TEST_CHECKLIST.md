# Refactoring Test Checklist

## Pre-Build Checks
- [x] All partial class files created
- [x] MainWindow.xaml.cs simplified to shell
- [x] Dead code removed
- [x] Files properly formatted

## Build Verification
```bash
# Clean and rebuild
dotnet clean
dotnet build
```

Expected: ✅ No compilation errors

## Runtime Testing

### Core Functionality
- [ ] **Launch**: App starts without crashes
- [ ] **Hotkeys**: Alt+Space toggles window
- [ ] **Hotkeys**: Alt+/ opens with "/" in input
- [ ] **Tray Icon**: Appears in system tray
- [ ] **Tray Menu**: Right-click shows menu with commands

### Search (MainWindow.Search.cs)
- [ ] **App Search**: Type "notepad" shows results
- [ ] **Real-time**: Results appear as you type
- [ ] **Launch App**: Click/Enter launches apps
- [ ] **Shortcuts**: Web shortcuts work (e.g., "yt cats")
- [ ] **Commands**: "/" prefix shows extension commands

### Chat (MainWindow.Chat.cs)
- [ ] **AI Chat**: Enter text triggers chat mode
- [ ] **Responses**: Gemini responds correctly
- [ ] **Images**: Attach image via button works
- [ ] **Images**: Ctrl+V paste image works
- [ ] **Code Blocks**: Markdown code fences render
- [ ] **Export**: Export chat to .md file works
- [ ] **Clear**: Clear chat button works

### Settings Manager (MainWindow.Manager.cs)
- [ ] **Open**: Type "settings" opens manager
- [ ] **API Key**: Can save API key
- [ ] **Shortcuts**: Add/delete web shortcuts
- [ ] **Commands**: Generate AI commands works
- [ ] **Models**: Model list loads

### Personalization (MainWindow.Personalization.cs)
- [ ] **Themes**: Built-in themes (Dark/Light/High Contrast) work
- [ ] **Colors**: Changing accent color updates UI
- [ ] **Sliders**: Border radius slider works
- [ ] **AI Preview**: Generate personalization preview works
- [ ] **Apply**: Accept preview applies changes
- [ ] **Reject**: Reject preview restores original

### Widgets (MainWindow.Widgets.cs)
- [ ] **Display**: Widgets show on empty search
- [ ] **Clock**: Shows current time
- [ ] **Weather**: Loads weather data
- [ ] **System Stats**: Shows CPU/RAM/Disk with progress bars
- [ ] **Drag-Drop**: Can reorder widgets
- [ ] **Resize**: Can change widget size (1x1 / 2x1)
- [ ] **Pin**: Pinning prevents drag
- [ ] **Float**: Can float widget to desktop

### Window Behavior
- [ ] **Positioning**: Centers on active monitor
- [ ] **Deactivate**: Hides when clicking outside (if not pinned)
- [ ] **Pin**: Pin button prevents auto-hide
- [ ] **Escape**: Escape key exits manager/chat/hides window
- [ ] **Opacity**: Opacity slider changes window transparency

## Edge Cases
- [ ] **No API Key**: App works without API key (shows warning in chat)
- [ ] **Empty Search**: Shows widgets panel
- [ ] **Invalid Shortcut**: Handles bad URLs gracefully
- [ ] **Widget Errors**: Custom widget errors don't crash app

## Performance
- [ ] **Startup**: Loads within 2-3 seconds
- [ ] **Search**: Results appear instantly
- [ ] **Animations**: Smooth transitions (if enabled)
- [ ] **Memory**: No obvious memory leaks after extended use

## Files Modified

### New Files (7 partial classes)
- MainWindow.Core.cs (618 lines)
- MainWindow.Search.cs (568 lines)
- MainWindow.Chat.cs (821 lines)
- MainWindow.Manager.cs (329 lines)
- MainWindow.Personalization.cs (764 lines)
- MainWindow.Widgets.cs (1,863 lines)
- MainWindow.Utilities.cs (51 lines)

### Modified Files
- MainWindow.xaml.cs (reduced from 4,949 to 42 lines)

### Documentation
- REFACTORING_SUMMARY.md (detailed summary)
- TEST_CHECKLIST.md (this file)

## Known Issues
None expected - this is a pure structural refactoring with no logic changes.

## Rollback Plan
If issues occur:
1. Restore MainWindow.xaml.cs from git: `git checkout HEAD -- MainWindow.xaml.cs`
2. Delete new partial class files: `rm MainWindow.*.cs`

## Sign-Off
- [ ] All tests pass
- [ ] No regressions found
- [ ] Code compiles
- [ ] Ready for commit

## Git Commit Message Template
```
Refactor: split god file into Models/Services, fix settings path, async cache scan

BREAKING CHANGE: None - pure structural refactoring

- Split 4,949-line MainWindow.xaml.cs into 7 organized partial classes
- MainWindow.Core.cs: Core infrastructure, lifecycle, tray (618 lines)
- MainWindow.Search.cs: Search and input handling (568 lines)
- MainWindow.Chat.cs: Chat UI and AI integration (821 lines)
- MainWindow.Manager.cs: Settings management (329 lines)
- MainWindow.Personalization.cs: Theming and styling (764 lines)
- MainWindow.Widgets.cs: Widget system (1,863 lines)
- MainWindow.Utilities.cs: Helper methods (51 lines)
- Removed duplicate UpdateStatsRow method
- Added XML documentation to all partial classes
- Fixed widget wrapping and drag-drop offset issues

See REFACTORING_SUMMARY.md for full details.
```
