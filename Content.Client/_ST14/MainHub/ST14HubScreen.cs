using System.Numerics;
using Content.Client.Changelog;
using Content.Client.GameTicking;
using Content.Client.Info;
using Content.Client.LateJoin;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Client.UserInterface.Systems.EscapeMenu;
using Content.Client.UserInterface.Systems.Guidebook;
using Content.Shared._ST14.MainHub;
using Content.Shared.CCVar;
using Content.Shared.GameTicking.Prototypes;
using Robust.Client;
using Robust.Client.Console;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._ST14.MainHub;

/// <summary>
/// Full screen main menu shown by ST14HubState right after connecting. Layout follows the main menu of Starship Troopers: Terran Command
/// </summary>
public sealed partial class ST14HubScreen : Control
{
    [Dependency] private IBaseClient _baseClient = default!;
    [Dependency] private IClientConsoleHost _console = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private IUriOpener _uriOpener = default!;
    [Dependency] private IUserInterfaceManager _uiManager = default!;

    private static readonly ProtoId<ST14HubScenePrototype>[] Wallpapers =
    {
        "ST14HubOrbit",
        "ST14HubRidge",
        "ST14HubFleet",
        "ST14HubMarauder",
        "ST14HubDesert",
        "ST14HubInferno",
    };

    private const int NewsCount = 3;
    private static readonly TimeSpan NewsInterval = TimeSpan.FromSeconds(8);

    // Bottom row of the original: the big card on the left, then plates every 212.68 px
    private const float RowLeft = 30.76f;
    private const float RowStep = 212.68f;
    private static readonly Vector2 PlateSize = new(187.68f, 70f);

    private readonly ST14AnimatedScene _scene;
    private readonly Label _newsLabel;
    private readonly Label _hourLabel;
    private readonly Label _minuteLabel;
    private readonly Label _dateLabel;
    private readonly Label _tempLabel;
    private readonly Label _statusLabel;
    private readonly TextureRect _thumbnail;
    private readonly ST14HubButton _warButton;
    private readonly ST14HubButton _observeButton;
    private readonly ST14SettingsScreen _settings;

    private int _newsIndex;
    private TimeSpan _newsElapsed;
    private ProtoId<LobbyBackgroundPrototype>? _shownBackground;

    public ST14HubScreen()
    {
        IoCManager.InjectDependencies(this);

        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;
        VerticalExpand = true;

        AddChild(new PanelContainer { PanelOverride = ST14HubStyle.Flat(Color.Black) });

        _scene = new ST14AnimatedScene { ScanlineAlpha = 0.3f };
        _scene.SetScenes(Wallpapers);
        AddChild(_scene);

        var canvas = new ST14CanvasLayout { ScaleLabels = true };
        AddChild(canvas);

        // Bottom bar and the status line above it
        canvas.Add(new PanelContainer { PanelOverride = ST14HubStyle.MainBar(_resCache) },
            new Vector2(0, 1), new Vector2(1, 1), Vector2.Zero, new Vector2(0, 93), new Vector2(0.5f, 1));

        _statusLabel = ST14HubStyle.Label(_resCache, string.Empty, ST14HubFont.Italic, 15, Color.White);
        _statusLabel.Align = Label.AlignMode.Center;
        _statusLabel.VAlign = Label.VAlignMode.Center;
        canvas.AddAt(_statusLabel, new Vector2(0.5f, 0.5f), new Vector2(0, 431), new Vector2(827, 32));

        // News banner
        canvas.Add(new TextureRect
        {
            Texture = ST14HubStyle.Tex(_resCache, "mask_bottom.png"),
            Stretch = TextureRect.StretchMode.Scale,
            ModulateSelfOverride = ST14HubStyle.NewsFade,
        }, Vector2.Zero, new Vector2(1, 0), Vector2.Zero, new Vector2(0, 230), new Vector2(0.5f, 0));

        canvas.Add(new TextureRect
        {
            Texture = ST14HubStyle.Tex(_resCache, "objectivebanner.png"),
            Stretch = TextureRect.StretchMode.Scale,
            ModulateSelfOverride = ST14HubStyle.NewsBar,
        }, Vector2.Zero, new Vector2(1, 0), new Vector2(0, 35), new Vector2(0, 50), new Vector2(0.5f, 0));

        canvas.AddAt(new TextureRect
        {
            Texture = ST14HubStyle.Tex(_resCache, "news_planet.png"),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
        }, Vector2.Zero, new Vector2(12, 60), new Vector2(70, 70), new Vector2(0, 0.5f));

        var newsRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 25,
        };
        var newsTitle = ST14HubStyle.Label(_resCache, Loc.GetString("st14-hub-news-title"), ST14HubFont.Regular, 27, ST14HubStyle.News);
        newsTitle.VAlign = Label.VAlignMode.Center;
        newsRow.AddChild(newsTitle);
        _newsLabel = ST14HubStyle.Label(_resCache, string.Empty, ST14HubFont.Regular, 27, ST14HubStyle.News);
        _newsLabel.VAlign = Label.VAlignMode.Center;
        _newsLabel.ClipText = true;
        _newsLabel.HorizontalExpand = true;
        newsRow.AddChild(_newsLabel);
        canvas.Add(newsRow, Vector2.Zero, new Vector2(1, 0), new Vector2(-176.5f, 60), new Vector2(-537, 50), new Vector2(0.5f, 0.5f));

        var moreButton = new ST14HubButton(_resCache, Loc.GetString("st14-hub-news-more"), 25, ST14HubStyle.Orange, ST14HubStyle.News);
        moreButton.OnPressed += _ => _uiManager.GetUIController<ChangelogUIController>().ToggleWindow();
        canvas.AddAt(moreButton, new Vector2(1, 0), new Vector2(-25, 60), new Vector2(400, 70), new Vector2(1, 0.5f));

        // Weather and clock widgets under the banner
        var weather = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _tempLabel = ST14HubStyle.Label(_resCache, string.Empty, ST14HubFont.Regular, 30, ST14HubStyle.Widget);
        _tempLabel.Align = Label.AlignMode.Center;
        weather.AddChild(_tempLabel);
        weather.AddChild(new TextureRect
        {
            Texture = ST14HubStyle.Tex(_resCache, "weather_sunny.png"),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            ModulateSelfOverride = ST14HubStyle.Widget,
            SetSize = new Vector2(60, 60),
            HorizontalAlignment = HAlignment.Center,
        });
        canvas.AddAt(weather, new Vector2(1, 0), new Vector2(-215, 136.5f), new Vector2(127.6f, 103.3f), new Vector2(1, 0));

        var clock = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        var time = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalAlignment = HAlignment.Center,
        };
        _hourLabel = ST14HubStyle.Label(_resCache, string.Empty, ST14HubFont.Regular, 40, ST14HubStyle.Widget);
        _minuteLabel = ST14HubStyle.Label(_resCache, string.Empty, ST14HubFont.Regular, 35, ST14HubStyle.Widget);
        _minuteLabel.VAlign = Label.VAlignMode.Bottom;
        time.AddChild(_hourLabel);
        time.AddChild(ST14HubStyle.Label(_resCache, ":", ST14HubFont.Regular, 40, ST14HubStyle.Widget));
        time.AddChild(_minuteLabel);
        clock.AddChild(time);
        clock.AddChild(new PanelContainer
        {
            PanelOverride = ST14HubStyle.Flat(ST14HubStyle.Widget),
            SetSize = new Vector2(150, 3.5f),
            HorizontalAlignment = HAlignment.Center,
        });
        _dateLabel = ST14HubStyle.Label(_resCache, string.Empty, ST14HubFont.Regular, 25, ST14HubStyle.Widget);
        _dateLabel.Align = Label.AlignMode.Center;
        clock.AddChild(_dateLabel);
        canvas.AddAt(clock, new Vector2(1, 0), new Vector2(-20.4f, 136.5f), new Vector2(170.6f, 103.3f), new Vector2(1, 0));

        // Social icons above the bottom bar
        AddLinkIcon(canvas, "discord.png", CCVars.InfoLinksDiscord, new Vector2(-84.2f, -298.2f), 125, 0.53f);
        AddLinkIcon(canvas, "twitter.png", CCVars.InfoLinksWebsite, new Vector2(-84.2f, -165.8f), 103, 0.67f);

        // "Continue" card, here it leads to the lobby and shows the lobby art
        var card = new ST14HubButton(_resCache, string.Empty, 20) { PressSound = ST14HubSound.Click };
        card.Label.Visible = false;
        var cardBox = new ST14CanvasLayout();
        _thumbnail = new TextureRect { Stretch = TextureRect.StretchMode.KeepAspectCovered, RectClipContent = true };
        cardBox.Add(_thumbnail, Vector2.Zero, Vector2.One, new Vector2(-1.7f, -12.9f), new Vector2(-26.7f, -47.9f));
        cardBox.Add(new TextureRect
        {
            Texture = ST14HubStyle.Tex(_resCache, "frame.png"),
            Stretch = TextureRect.StretchMode.Scale,
        }, Vector2.Zero, Vector2.One, new Vector2(-1.7f, -12.9f), new Vector2(-26.7f, -47.9f));
        var cardText = ST14HubStyle.Label(_resCache, Loc.GetString("st14-hub-button-lobby").ToUpperInvariant(), ST14HubFont.Regular, 20, Color.White, shadow: true);
        cardText.Align = Label.AlignMode.Center;
        cardText.VAlign = Label.VAlignMode.Center;
        cardBox.Add(cardText, new Vector2(0, 1), new Vector2(1, 1), new Vector2(-3.75f, -12.5f), new Vector2(-7.5f, 26), new Vector2(0.5f, 1));
        card.AddChild(cardBox);
        card.OnPressed += _ => HubController.EnterLobby();
        canvas.AddAt(card, new Vector2(0, 1), new Vector2(RowLeft + 93.84f, -107.3f), new Vector2(187.68f, 176.5f));

        _warButton = AddPlate(canvas, 0, "st14-hub-button-war", ST14HubStyle.Blue, ST14HubStyle.BlueText);
        _warButton.OnPressed += _ => GoToWar();

        AddPlate(canvas, 1, "st14-hub-button-character").OnPressed += _ => OpenCharacterSetup();

        _observeButton = AddPlate(canvas, 2, "st14-hub-button-observe");
        _observeButton.OnPressed += _ => new ObserveWarningWindow().OpenCentered();

        AddPlate(canvas, 3, "st14-hub-button-guidebook").OnPressed +=
            _ => _uiManager.GetUIController<GuidebookUIController>().ToggleGuidebook();

        AddPlate(canvas, 4, "st14-hub-rules").OnPressed += _ => new RulesAndInfoWindow().Open();

        var settingsButton = new ST14HubIconButton(ST14HubStyle.Tex(_resCache, "gear.png"), ST14HubStyle.Gold);
        settingsButton.OnPressed += _ => _settings!.Show();
        canvas.AddAt(settingsButton, new Vector2(1, 1), new Vector2(-122.5f, -25), new Vector2(60, 60), new Vector2(1, 1));

        var exitButton = new ST14HubIconButton(ST14HubStyle.Tex(_resCache, "power.png"), ST14HubStyle.Gold)
        {
            PressSound = ST14HubSound.Back,
        };
        exitButton.OnPressed += _ => _console.ExecuteCommand("disconnect");
        canvas.AddAt(exitButton, new Vector2(1, 1), new Vector2(-37.5f, -25), new Vector2(60, 60), new Vector2(1, 1));

        // Settings cover the hub when opened
        _settings = new ST14SettingsScreen { Visible = false };
        _settings.OnClosed += () => _scene.Visible = true;
        _settings.OnOpened += () => _scene.Visible = false;
        AddChild(_settings);

        ShowNews(0);
        UpdateClock();
    }

    private ST14HubButton AddPlate(ST14CanvasLayout canvas, int index, string loc, Color? tint = null, Color? textColor = null)
    {
        var button = new ST14HubButton(_resCache, Loc.GetString(loc), 21, tint, textColor);
        var x = RowLeft + 306.52f + RowStep * index;
        canvas.AddAt(button, new Vector2(0, 1), new Vector2(x, -54), PlateSize);
        return button;
    }

    private void AddLinkIcon(ST14CanvasLayout canvas, string texture, CVarDef<string> cvar, Vector2 position, float size, float alpha)
    {
        var url = _cfg.GetCVar(cvar);
        if (string.IsNullOrEmpty(url))
            return;

        var icon = new ST14HubIconButton(ST14HubStyle.Tex(_resCache, texture), Color.White.WithAlpha(alpha));
        icon.OnPressed += _ => _uriOpener.OpenUri(url);
        canvas.AddAt(icon, new Vector2(1, 1), position, new Vector2(size, size));
    }

    private ClientGameTicker GameTicker => _entMan.System<ClientGameTicker>();

    private ST14HubUIController HubController => _uiManager.GetUIController<ST14HubUIController>();

    private void GoToWar()
    {
        if (GameTicker.IsGameStarted)
        {
            new LateJoinGui().OpenCentered();
            return;
        }

        // Same as pressing the ready toggle in the lobby
        _console.ExecuteCommand("toggleready true");
        if (HubController.EnterLobby().Lobby is not { } lobby)
            return;

        lobby.ReadyButton.Pressed = true;
        lobby.ReadyButton.Text = Loc.GetString("lobby-state-player-status-ready");
    }

    private void OpenCharacterSetup()
    {
        HubController.EnterLobby().SwitchState(LobbyGui.LobbyGuiState.CharacterSetup);
    }

    // The card shows the same background the lobby would show
    private void UpdateThumbnail()
    {
        var current = GameTicker.LobbyBackground;
        if (current == _shownBackground)
            return;

        _shownBackground = current;
        _thumbnail.Texture = _protoMan.TryIndex(current, out var proto)
            ? _resCache.GetResource<TextureResource>(proto.Background).Texture
            : null;
    }

    private void ShowNews(int index)
    {
        _newsIndex = index % NewsCount;
        _newsLabel.Text = Loc.GetString($"st14-hub-news-{_newsIndex + 1}");
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        _hourLabel.Text = $"{now.Hour:D2}";
        _minuteLabel.Text = $"{now.Minute:D2}";
        _dateLabel.Text = Loc.GetString("st14-hub-date", ("day", now.Day), ("month", Loc.GetString($"st14-hub-month-{now.Month}")));

        // The original shows a made up forecast as well, keep it stable for the day
        _tempLabel.Text = Loc.GetString("st14-hub-temperature", ("degrees", 18 + now.DayOfYear * 7 % 13));
    }

    private void UpdateStatus()
    {
        var serverName = _baseClient.GameInfo?.ServerName ?? string.Empty;
        var started = GameTicker.IsGameStarted;

        _statusLabel.Text = Loc.GetString(started ? "st14-hub-status-round" : "st14-hub-status-lobby",
            ("server", serverName));

        _observeButton.Disabled = !started;
        _warButton.Text = Loc.GetString(started ? "st14-hub-button-war-join" : "st14-hub-button-war");
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!Visible)
            return;

        UpdateThumbnail();

        _newsElapsed += TimeSpan.FromSeconds(args.DeltaSeconds);
        if (_newsElapsed >= NewsInterval)
        {
            _newsElapsed = TimeSpan.Zero;
            ShowNews(_newsIndex + 1);
        }

        UpdateClock();
        UpdateStatus();
    }
}
