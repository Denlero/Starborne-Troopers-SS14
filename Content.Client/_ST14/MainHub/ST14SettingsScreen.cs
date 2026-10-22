using System.Numerics;
using Content.Client.Options.UI.Tabs;
using Content.Shared._ST14.MainHub;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._ST14.MainHub;

/// <summary>
/// Full screen settings like the options menu of the original: animated background per tab, a framed window and categories in the bottom bar.
/// Reuses the regular options tabs so saving works the same
/// </summary>
public sealed partial class ST14SettingsScreen : Control
{
    [Dependency] private IResourceCache _resCache = default!;

    private readonly ST14AnimatedScene _scene;
    private readonly ST14CanvasLayout _canvas;
    private readonly List<SettingsTab> _tabs = new();
    private SettingsTab? _current;

    public event Action? OnOpened;
    public event Action? OnClosed;

    public ST14SettingsScreen()
    {
        IoCManager.InjectDependencies(this);

        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;
        VerticalExpand = true;

        AddChild(new PanelContainer { PanelOverride = ST14HubStyle.Flat(Color.Black) });

        _scene = new ST14AnimatedScene();
        AddChild(_scene);

        _canvas = new ST14CanvasLayout { ScaleLabels = true };
        AddChild(_canvas);

        _canvas.Add(new PanelContainer { PanelOverride = ST14HubStyle.MainBar(_resCache) },
            new Vector2(0, 1), new Vector2(1, 1), Vector2.Zero, new Vector2(0, 93), new Vector2(0.5f, 1));

        // Window rects of the original options menu, the audio one is taller since there are more sliders here
        AddTab(0, "preferences", "options_preferences.png", "ST14HubSettingsPreferences",
            new Vector2(431, -49.4f), new Vector2(560, 715), () => new MiscTab(), t => ((MiscTab) t).Control.ReloadValues());
        AddTab(1, "controls", "options_graphics.png", "ST14HubSettingsControls",
            new Vector2(220.75f, 1), new Vector2(1264.5f, 698), () => new KeyRebindTab(), null);
        AddTab(2, "graphics", "options_controls.png", "ST14HubSettingsGraphics",
            new Vector2(-505, -18.6f), new Vector2(560, 767), () => new GraphicsTab(), t => ((GraphicsTab) t).Control.ReloadValues());
        AddTab(3, "audio", "options_audio.png", "ST14HubSettingsAudio",
            new Vector2(390.5f, 45.5f), new Vector2(850.6f, 520), () => new AudioTab(), t => ((AudioTab) t).Control.ReloadValues());

        var close = new ST14HubIconButton(ST14HubStyle.Tex(_resCache, "exit.png"), ST14HubStyle.Gold)
        {
            PressSound = ST14HubSound.Back,
        };
        close.OnPressed += _ => Close();
        _canvas.AddAt(close, new Vector2(1, 1), new Vector2(-125.6f, -13.8f), new Vector2(54.4f, 57), new Vector2(1, 1));
    }

    public void Show()
    {
        Visible = true;
        Select(_current ?? _tabs[0]);
        OnOpened?.Invoke();
    }

    public void Close()
    {
        Visible = false;
        OnClosed?.Invoke();
    }

    private void AddTab(int index, string id, string icon, ProtoId<ST14HubScenePrototype> scene, Vector2 position, Vector2 size,
        Func<Control> factory, Action<Control>? reload)
    {
        var button = new ST14HubCategoryButton(_resCache, Loc.GetString($"st14-hub-settings-tab-{id}"), ST14HubStyle.Tex(_resCache, icon));
        _canvas.AddAt(button, new Vector2(0, 1), new Vector2(217.5f + 435 * index, -42.3f), new Vector2(300, 57));

        var tab = new SettingsTab(id, scene, position, size, button, factory, reload);
        button.OnPressed += _ => Select(tab);
        _tabs.Add(tab);
    }

    private void Select(SettingsTab tab)
    {
        var changed = _current != tab;
        _current = tab;

        foreach (var other in _tabs)
        {
            other.Button.Selected = other == tab;
            if (other.Panel != null)
                other.Panel.Visible = other == tab;
        }

        // Tabs are built on first open, this keeps the hub cheap when nobody opens settings
        if (tab.Panel == null)
        {
            var panel = new PanelContainer { PanelOverride = ST14HubStyle.Window(_resCache) };
            var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };

            var title = ST14HubStyle.Label(_resCache, Loc.GetString($"st14-hub-settings-panel-{tab.Id}").ToUpperInvariant(),
                ST14HubFont.SemiBold, 21, ST14HubStyle.GoldLight);
            title.Align = Label.AlignMode.Center;
            title.MinHeight = 36;
            body.AddChild(title);

            tab.Contents = tab.Factory();
            tab.Contents.VerticalExpand = true;
            tab.Contents.HorizontalExpand = true;
            tab.Contents.Margin = new Thickness(0, 10, 0, 0);
            RestyleSliders(tab.Contents);
            body.AddChild(tab.Contents);

            panel.AddChild(body);
            tab.Panel = panel;
            _canvas.AddAt(panel, new Vector2(0.5f, 0.5f), tab.Position, tab.Size);
        }

        tab.Panel.Visible = true;
        tab.Reload?.Invoke(tab.Contents!);

        if (changed)
            _scene.SetScene(tab.Scene);
    }

    /// <summary>
    /// Sliders of the original are a dark groove with a brass fill
    /// </summary>
    private static void RestyleSliders(Control control)
    {
        if (control is Slider slider)
        {
            slider.BackgroundStyleBoxOverride = ST14HubStyle.Flat(Color.FromHex("#14130F"), ST14HubStyle.Gold.WithAlpha(0.6f), 1);
            slider.ForegroundStyleBoxOverride = ST14HubStyle.Flat(Color.Transparent, ST14HubStyle.Gold.WithAlpha(0.6f), 1);
            slider.FillStyleBoxOverride = ST14HubStyle.Flat(ST14HubStyle.Gold);
            var grabber = ST14HubStyle.Flat(ST14HubStyle.GoldLight, Color.FromHex("#3A2E14"), 1);
            grabber.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6);
            slider.GrabberStyleBoxOverride = grabber;
        }

        foreach (var child in control.Children)
        {
            RestyleSliders(child);
        }
    }

    private sealed class SettingsTab(
        string id,
        ProtoId<ST14HubScenePrototype> scene,
        Vector2 position,
        Vector2 size,
        ST14HubCategoryButton button,
        Func<Control> factory,
        Action<Control>? reload)
    {
        public readonly string Id = id;
        public readonly ProtoId<ST14HubScenePrototype> Scene = scene;
        public readonly Vector2 Position = position;
        public readonly Vector2 Size = size;
        public readonly ST14HubCategoryButton Button = button;
        public readonly Func<Control> Factory = factory;
        public readonly Action<Control>? Reload = reload;
        public PanelContainer? Panel;
        public Control? Contents;
    }
}

/// <summary>
/// Square icon plate with a caption, the category switch in the bottom bar of the settings
/// </summary>
public sealed class ST14HubCategoryButton : ContainerButton
{
    private readonly IResourceCache _cache;
    private readonly PanelContainer _plate;
    private readonly TextureRect _icon;
    private readonly Label _label;
    private bool _selected;

    public ST14HubCategoryButton(IResourceCache cache, string text, Texture icon)
    {
        _cache = cache;
        MuteSounds = true;

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
        };

        _plate = new PanelContainer { SetSize = new Vector2(57, 57) };
        _icon = new TextureRect
        {
            Texture = icon,
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            Margin = new Thickness(6),
        };
        _plate.AddChild(_icon);
        row.AddChild(_plate);

        _label = ST14HubStyle.Label(cache, text.ToUpperInvariant(), ST14HubFont.Regular, 22, ST14HubStyle.Gold, shadow: true);
        _label.VAlign = Label.VAlignMode.Center;
        _label.HorizontalExpand = true;
        _label.Align = Label.AlignMode.Center;
        row.AddChild(_label);

        AddChild(row);
        OnPressed += _ => Sounds.Play(ST14HubSound.Tab);
        UpdateLook();
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            UpdateLook();
        }
    }

    private ST14HubSoundController Sounds => UserInterfaceManager.GetUIController<ST14HubSoundController>();

    protected override void MouseEntered()
    {
        base.MouseEntered();
        Sounds.Play(ST14HubSound.Hover);
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        UpdateLook();
    }

    private void UpdateLook()
    {
        if (_label == null)
            return;

        var lit = _selected || DrawMode is DrawModeEnum.Hover or DrawModeEnum.Pressed;
        _plate.PanelOverride = ST14HubStyle.UnitButton(_cache, lit ? Color.White : Color.FromHex("#B8B8B8"));
        _icon.ModulateSelfOverride = lit ? Color.White : Color.FromHex("#C8C8C8");
        _label.FontColorOverride = lit ? ST14HubStyle.GoldLight : ST14HubStyle.Gold;
    }
}
