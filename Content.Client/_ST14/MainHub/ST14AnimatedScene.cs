using System.Numerics;
using Content.Shared._ST14.MainHub;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._ST14.MainHub;

/// <summary>
/// Draws a layered <see cref="ST14HubScenePrototype"/> scaled to cover the control.
/// With several scenes it plays each one and switches to the next through the glitch of the original menu, the old one keeps moving until the switch
/// </summary>
public sealed partial class ST14AnimatedScene : Control
{
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private IResourceCache _cache = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IClyde _clyde = default!;

    private const string ScanlinesPath = "/Textures/_ST14/Interface/Hub/Settings/scanlines.png";
    // GlitchTransition of the original camera: 0.2 s up to full strength, the wallpaper changes at the peak, 0.2 s back down
    private const float GlitchIn = 0.2f;
    private const float GlitchOut = 0.2f;
    private static readonly ProtoId<ShaderPrototype> GlitchShader = "ST14HubGlitch";

    private static readonly Color SloganColor = Color.FromHex("#FFCC7C");
    private static readonly Color SloganOutline = Color.FromHex("#3A1E00");

    private readonly List<SceneData> _playlist = new();
    private int _current;
    private float _time;
    private float _glitch = -1;
    private float _clock;
    private Texture? _scanlines;
    private ShaderInstance? _glitchShader;
    private IRenderTexture? _target;

    /// <summary>
    /// Extra seconds a scene stays after its animation ends before switching
    /// </summary>
    public float Hold;

    /// <summary>
    /// Alpha of the scanline overlay drawn over the whole scene, zero disables it
    /// </summary>
    public float ScanlineAlpha;

    public ST14AnimatedScene()
    {
        IoCManager.InjectDependencies(this);
        HorizontalExpand = true;
        VerticalExpand = true;
        RectClipContent = true;
    }

    /// <summary>
    /// Loads every scene and its textures up front, so switching never stalls on the first draw
    /// </summary>
    public void SetScenes(IEnumerable<ProtoId<ST14HubScenePrototype>> scenes, bool shuffle = true)
    {
        _playlist.Clear();
        foreach (var id in scenes)
        {
            if (_protoMan.TryIndex(id, out var proto))
                _playlist.Add(new SceneData(proto, _cache));
        }

        if (shuffle)
            _random.Shuffle(_playlist);

        _current = 0;
        _time = 0;
        _glitch = -1;
    }

    public void SetScene(ProtoId<ST14HubScenePrototype> scene)
    {
        SetScenes(new[] { scene }, false);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!VisibleInTree || _playlist.Count == 0)
            return;

        // Real frame time, so the motion is continuous whatever the tick rate is
        _clock += args.DeltaSeconds;
        _time += args.DeltaSeconds;

        if (_glitch >= 0)
        {
            var before = _glitch;
            _glitch += args.DeltaSeconds;

            // the old wallpaper keeps moving until the peak, then the next one starts from its first frame
            if (before < GlitchIn && _glitch >= GlitchIn)
            {
                _current = (_current + 1) % _playlist.Count;
                _time = 0;
            }

            if (_glitch >= GlitchIn + GlitchOut)
                _glitch = -1;
        }

        var scene = _playlist[_current].Proto;
        if (_playlist.Count > 1 && _glitch < 0 && scene.Duration > 0 && _time >= scene.Duration + Hold)
            _glitch = 0;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_playlist.Count == 0)
            return;

        var baseTransform = handle.GetTransform();
        Vector2 size = PixelSize;
        var scene = _playlist[_current];
        var amount = GlitchAmount();

        if (amount > 0 && PixelSize.X > 0 && PixelSize.Y > 0)
        {
            // the glitch works on the finished picture, so the scene goes to an offscreen texture first
            if (_target == null || _target.Size != PixelSize)
            {
                _target?.Dispose();
                _target = _clyde.CreateRenderTarget(PixelSize,
                    new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                    new TextureSampleParameters { Filter = true },
                    nameof(ST14AnimatedScene));
            }

            handle.RenderInRenderTarget(_target, () =>
            {
                DrawScene(handle, scene, _time, 1f, size, Matrix3x2.Identity);
                handle.SetTransform(Matrix3x2.Identity);
            }, Color.Black);

            _glitchShader ??= _protoMan.Index(GlitchShader).InstanceUnique();
            _glitchShader.SetParameter("amount", amount);
            _glitchShader.SetParameter("screenSize", size);

            handle.SetTransform(baseTransform);
            handle.UseShader(_glitchShader);
            handle.DrawTextureRect(_target.Texture, new UIBox2(Vector2.Zero, size));
            handle.UseShader(null);
        }
        else
        {
            DrawScene(handle, scene, _time, 1f, size, baseTransform);
        }

        handle.SetTransform(baseTransform);

        if (ScanlineAlpha > 0)
        {
            _scanlines ??= _cache.GetResource<TextureResource>(ScanlinesPath).Texture;
            var scale = MathF.Max(size.X / ST14CanvasLayout.CanvasSize.X, size.Y / ST14CanvasLayout.CanvasSize.Y);
            DrawTiled(handle, _scanlines, new UIBox2(Vector2.Zero, size), Color.Black.WithAlpha(ScanlineAlpha), scale);
        }
    }

    private float GlitchAmount()
    {
        if (_glitch < 0)
            return 0f;

        return _glitch < GlitchIn
            ? _glitch / GlitchIn
            : MathF.Max(0f, 1f - (_glitch - GlitchIn) / GlitchOut);
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        _target?.Dispose();
        _target = null;
    }

    private void DrawScene(DrawingHandleScreen handle, SceneData scene, float time, float sceneAlpha, Vector2 size, Matrix3x2 baseTransform)
    {
        if (sceneAlpha <= 0)
            return;

        var proto = scene.Proto;
        var layers = proto.Layers;
        var canvas = proto.CanvasSize;
        var scale = MathF.Max(size.X / canvas.X, size.Y / canvas.Y);
        var offset = (size - canvas * scale) / 2;
        var canvasMatrix = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(offset);
        var rootRect = new UIBox2(-proto.Pivot * canvas, (Vector2.One - proto.Pivot) * canvas);
        var rootMatrix = Matrix3x2.CreateTranslation(proto.Pivot * canvas) * canvasMatrix;

        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            var parent = scene.Parents[i];
            var parentRect = parent >= 0 ? scene.Rects[parent] : rootRect;
            var parentMatrix = parent >= 0 ? scene.Matrices[parent] : rootMatrix;

            var position = layer.Position;
            var sizeDelta = layer.Size;
            var pivot = layer.Pivot;
            var rotation = layer.Rotation;
            var layerScale = layer.Scale;
            var alpha = 1f;

            foreach (var curve in layer.Curves)
            {
                // past the end the motion keeps its last speed, so a scene fading out never freezes
                var value = curve.Evaluate(time, extrapolate: true);
                switch (curve.Property)
                {
                    case ST14HubSceneProperty.PosX: position.X = value; break;
                    case ST14HubSceneProperty.PosY: position.Y = value; break;
                    case ST14HubSceneProperty.SizeX: sizeDelta.X = value; break;
                    case ST14HubSceneProperty.SizeY: sizeDelta.Y = value; break;
                    case ST14HubSceneProperty.Rotation: rotation = value; break;
                    case ST14HubSceneProperty.PivotX: pivot.X = value; break;
                    case ST14HubSceneProperty.PivotY: pivot.Y = value; break;
                    case ST14HubSceneProperty.ScaleX: layerScale.X = value; break;
                    case ST14HubSceneProperty.ScaleY: layerScale.Y = value; break;
                    case ST14HubSceneProperty.Alpha: alpha = Math.Clamp(value, 0f, 1f); break;
                }
            }

            if (layer.Wiggle is { } wiggle)
            {
                position += new Vector2(
                    Noise(_clock * wiggle.Frequency.X, i * 3.1f) * wiggle.Amplitude.X,
                    Noise(_clock * wiggle.Frequency.Y, i * 3.1f + 1.7f) * wiggle.Amplitude.Y);

                if (wiggle.RotationFrequency > 0)
                    rotation += Noise(_clock * wiggle.RotationFrequency, i * 3.1f + 5.3f) * wiggle.Rotation;
            }

            Vector2 pivotPos;
            UIBox2 rect;
            if (layer.Rect)
            {
                var anchorMin = parentRect.TopLeft + parentRect.Size * layer.AnchorMin;
                var anchorMax = parentRect.TopLeft + parentRect.Size * layer.AnchorMax;
                var rectSize = anchorMax - anchorMin + sizeDelta;
                pivotPos = Vector2.Lerp(anchorMin, anchorMax, pivot) + position;
                rect = new UIBox2(-rectSize * pivot, rectSize * (Vector2.One - pivot));
            }
            else
            {
                pivotPos = position;
                rect = default;
            }

            // all in floats, nothing is snapped to whole pixels
            var matrix = Matrix3x2.CreateScale(layerScale)
                         * Matrix3x2.CreateRotation(MathHelper.DegreesToRadians(rotation))
                         * Matrix3x2.CreateTranslation(pivotPos)
                         * parentMatrix;

            scene.Rects[i] = rect;
            scene.Matrices[i] = matrix;

            if (!layer.Rect)
                continue;

            var color = layer.Color.WithAlpha(layer.Color.A * alpha * sceneAlpha);
            if (color.A <= 0)
                continue;

            if (layer.Text is { } text)
            {
                DrawText(handle, scene, i, text, rect, matrix, scale, baseTransform, color.A);
                continue;
            }

            var texture = scene.Textures[i];
            if (texture == null && !layer.Solid)
                continue;

            handle.SetTransform(matrix * baseTransform);

            if (texture == null)
                handle.DrawRect(rect, color);
            else if (layer.Tile)
                DrawTiled(handle, texture, rect, color, 1f);
            else
                handle.DrawTextureRect(texture, rect, color);
        }
    }

    private void DrawText(DrawingHandleScreen handle, SceneData scene, int index, ST14HubSceneText text, UIBox2 rect,
        Matrix3x2 matrix, float scale, Matrix3x2 baseTransform, float alpha)
    {
        // Text is rendered at screen resolution, so only the rotation and the center come from the layer matrix
        var center = Vector2.Transform(rect.Center, matrix);
        var right = Vector2.Transform(rect.Center + Vector2.UnitX, matrix) - center;
        var angle = MathF.Atan2(right.Y, right.X);

        var fontSize = Math.Max(8, (int) MathF.Round(text.Size * scale));
        var font = ST14HubStyle.Font(_cache, ST14HubFont.Slogan, fontSize);
        var lines = scene.TextLines[index] ??= Loc.GetString(text.Loc).Split('\n');
        var widths = scene.TextWidths[index];
        if (widths == null || scene.TextFontSizes[index] != fontSize)
        {
            widths = new float[lines.Length];
            for (var l = 0; l < lines.Length; l++)
            {
                widths[l] = handle.GetDimensions(font, lines[l], 1f).X;
            }

            scene.TextWidths[index] = widths;
            scene.TextFontSizes[index] = fontSize;
        }

        var lineHeight = font.GetLineHeight(1f);
        var thickness = MathF.Max(2f, fontSize * 0.07f);
        var shadow = Color.Black.WithAlpha(0.6f * alpha);

        handle.SetTransform(Matrix3x2.CreateRotation(angle) * Matrix3x2.CreateTranslation(center) * baseTransform);

        var top = -lines.Length * lineHeight / 2f;
        for (var l = 0; l < lines.Length; l++)
        {
            var pos = new Vector2(-widths[l] / 2f, top);

            // drop shadow, then the outlined golden face
            handle.DrawString(font, pos + new Vector2(fontSize * 0.06f), lines[l], 1f, shadow, new TextOutline(thickness, shadow));
            handle.DrawString(font, pos, lines[l], 1f, SloganColor.WithAlpha(alpha), new TextOutline(thickness, SloganOutline.WithAlpha(alpha)));
            top += lineHeight;
        }
    }

    /// <summary>
    /// One quad with repeating texture coordinates, the tiled textures are loaded with wrap: repeat
    /// </summary>
    private static void DrawTiled(DrawingHandleScreen handle, Texture texture, UIBox2 rect, Color color, float scale)
    {
        var region = new UIBox2(0, 0, rect.Width / scale, rect.Height / scale);
        handle.DrawTextureRectRegion(texture, rect, region, color);
    }

    /// <summary>
    /// Smooth noise in [-1, 1] from continuous time, close enough to the Perlin wiggle of the original
    /// </summary>
    private static float Noise(float x, float seed)
    {
        return (MathF.Sin(x * MathF.Tau + seed)
                + 0.5f * MathF.Sin(x * MathF.Tau * 2.17f + seed * 1.9f)
                + 0.25f * MathF.Sin(x * MathF.Tau * 4.31f + seed * 3.7f)) / 1.75f;
    }

    private sealed class SceneData
    {
        public readonly ST14HubScenePrototype Proto;
        public readonly Texture?[] Textures;
        public readonly int[] Parents;
        public readonly UIBox2[] Rects;
        public readonly Matrix3x2[] Matrices;
        public readonly string[]?[] TextLines;
        public readonly float[]?[] TextWidths;
        public readonly int[] TextFontSizes;

        public SceneData(ST14HubScenePrototype proto, IResourceCache cache)
        {
            Proto = proto;
            var count = proto.Layers.Count;
            Textures = new Texture?[count];
            Parents = new int[count];
            Rects = new UIBox2[count];
            Matrices = new Matrix3x2[count];
            TextLines = new string[count][];
            TextWidths = new float[count][];
            TextFontSizes = new int[count];

            var index = new Dictionary<string, int>();
            for (var i = 0; i < count; i++)
            {
                var layer = proto.Layers[i];
                index[layer.Id] = i;
                Parents[i] = layer.Parent != null && index.TryGetValue(layer.Parent, out var parent) ? parent : -1;

                if (layer.Texture is { } path && cache.TryGetResource<TextureResource>(path, out var texture))
                    Textures[i] = texture.Texture;
            }
        }
    }
}
