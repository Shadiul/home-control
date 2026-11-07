using System.Windows.Input;
using H.NotifyIcon;
using LedControl.Core;
using Microsoft.UI.Xaml.Controls;

namespace LedControl.Shell;

sealed class RelayCommand(Action run) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => run();
}

/// <summary>Notification-area icon: click opens the quick panel, right-click gives a menu of quick actions.</summary>
sealed class Tray : IDisposable
{
    readonly TaskbarIcon icon;
    readonly MenuFlyoutItem powerItem, musicItem;
    readonly MenuFlyoutSeparator acSeparator = new();
    readonly List<(AcUnit Unit, MenuFlyoutSubItem Menu, MenuFlyoutItem Power)> acItems = [];
    readonly MenuFlyout menu;
    readonly App app;

    public bool IsCreated => icon.IsCreated;

    public Tray(App app)
    {
        this.app = app;
        menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Open Home Control", Command = OnUi(app.Window.ShowAndFocus) });
        menu.Items.Add(new MenuFlyoutSeparator());
        powerItem = new MenuFlyoutItem { Text = "Turn on", Command = OnUi(app.Session.TogglePower) };
        menu.Items.Add(powerItem);
        var scenes = new MenuFlyoutSubItem { Text = "Scenes" };
        foreach (var s in LightSession.Scenes)
        {
            var cmd = s.Command;
            scenes.Items.Add(new MenuFlyoutItem { Text = s.Name, Command = OnUi(() => app.Session.Submit(cmd)) });
        }
        menu.Items.Add(scenes);
        musicItem = new MenuFlyoutItem { Text = "Start music sync", Command = OnUi(app.Session.ToggleMusic) };
        menu.Items.Add(musicItem);
        menu.Items.Add(acSeparator);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Quit", Command = OnUi(app.Quit) });

        icon = new TaskbarIcon
        {
            ToolTipText = AppInfo.Name,
            Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico")),
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            NoLeftClickDelay = true,
            LeftClickCommand = OnUi(app.ToggleQuickPanel),
        };
        icon.ForceCreate(false); // false: don't put the app in Windows' efficiency mode (music sync needs the CPU)

        app.Session.Changed += Update;
        app.Session.MusicChanged += Update;
        app.Acs.Changed += Update;
        app.Acs.ListChanged += BuildAcItems;
        BuildAcItems();
    }

    /// <summary>One submenu per air conditioner, between music sync and Quit.</summary>
    void BuildAcItems()
    {
        foreach (var (_, m, _) in acItems) menu.Items.Remove(m);
        acItems.Clear();
        int at = menu.Items.IndexOf(acSeparator) + 1;
        foreach (var u in app.Acs.Units)
        {
            var unit = u;
            var sub = new MenuFlyoutSubItem { Text = unit.Config.DisplayName };
            var power = new MenuFlyoutItem { Text = "Turn on", Command = OnUi(unit.TogglePower) };
            sub.Items.Add(power);
            sub.Items.Add(new MenuFlyoutItem
            {
                Text = "Cool to 24 °C",
                Command = OnUi(() => unit.Set(("Pow", 1), ("Mod", AcState.Cool), ("SetTem", 24), ("TemUn", 0))),
            });
            sub.Items.Add(new MenuFlyoutSeparator());
            sub.Items.Add(new MenuFlyoutItem { Text = "More settings…", Command = OnUi(() => app.Window.ShowAc(unit)) });
            menu.Items.Insert(at++, sub);
            acItems.Add((unit, sub, power));
        }
        acSeparator.Visibility = acItems.Count > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        Update();
    }

    /// <summary>Tray clicks can arrive on the icon's message thread: run actions on the UI thread.</summary>
    RelayCommand OnUi(Action action) => new(() => app.UI.TryEnqueue(() => action()));

    void Update()
    {
        var st = app.Session.Shown;
        powerItem.Text = st?.On == true ? "Turn off" : "Turn on";
        musicItem.Text = app.Session.MusicRunning ? "Stop music sync" : "Start music sync";
        icon.ToolTipText = st == null ? AppInfo.Name : $"{AppInfo.Name} · Light: {st.Describe()}";
        foreach (var (unit, sub, power) in acItems)
        {
            var ac = unit.Shown;
            sub.Text = ac == null ? unit.Config.DisplayName : $"{unit.Config.DisplayName} · {ac.Describe()}";
            power.Text = ac?.On == true ? "Turn off" : "Turn on";
        }
    }

    public void Dispose() => icon.Dispose();
}
