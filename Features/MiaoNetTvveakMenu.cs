using FMOD.Studio;

namespace Celeste.Mod.MiaoNetTvveak;

public static class MiaoNetTvveakMenu
{
    private static List<FadeVisibleItem>? _featureItems;

    public static void Create(TextMenu menu, bool inGame, EventInstance snapshot)
    {
        var settings = MiaoNetTvveakModule.Settings;

        menu.Add(new TextMenu.SubHeader(
            Dialog.Clean("modoptions_miaonettvveaksettings_title")
            + " | v." + MiaoNetTvveakModule.Instance.Metadata.VersionString
        ));

        _featureItems = [];

        menu.Add(new TextMenu.OnOff(Dialog.Clean("modoptions_miaonettvveaksettings_enabled"), settings.Enabled)
            .Change(value =>
            {
                settings.Enabled = value;
                if (_featureItems is not null)
                {
                    foreach (var item in _featureItems)
                        item.FadeVisible = value;
                }
            })
        );

        AddFeatureToggle(menu, settings,
            nameof(settings.GhostNameBaking),
            v => settings.GhostNameBaking = v);

        menu.OnClose += () => _featureItems = null;
    }

    private static void AddFeatureToggle(
        TextMenu menu, MiaoNetTvveakSettings settings,
        string settingName, Action<bool> onChange)
    {
        string labelKey = $"modoptions_miaonettvveaksettings_{settingName.ToLowerInvariant()}";
        string tipKey = labelKey + "_tip";

        bool currentValue = (bool)settings.GetType().GetProperty(settingName)!.GetValue(settings)!;
        var item = new FadeVisibleItem(Dialog.Clean(labelKey), currentValue)
        {
            FadeVisible = settings.Enabled,
        };
        item.Change(v =>
        {
            onChange(v);
            MiaoNetTvveakModule.Instance.SaveSettings();
        });

        _featureItems!.Add(item);
        menu.Add(item);

        // 别忘了 AddDescription 必须在 menu.Add(item) 之后调用
        string tip = Dialog.Clean(tipKey);
        if (tip != tipKey)
            item.AddDescription(menu, tip);
    }

    internal class FadeVisibleItem : TextMenu.OnOff
    {
        public FadeVisibleItem(string label, bool on) : base(label, on) { }

        public bool FadeVisible { get; set; } = true;
        private float _alpha = 1f;
        private float _unEasedAlpha = 1f;

        public override void Update()
        {
            base.Update();

            float target = FadeVisible ? 1f : 0f;
            if (Math.Abs(_unEasedAlpha - target) > 0.001f)
            {
                _unEasedAlpha = Calc.Approach(_unEasedAlpha, target, Engine.RawDeltaTime * 3f);
                _alpha = FadeVisible ? Ease.SineOut(_unEasedAlpha) : Ease.SineIn(_unEasedAlpha);
            }

            Visible = _alpha > 0f;
        }

        public override float Height()
            => MathHelper.Lerp(-Container.ItemSpacing, base.Height(), _alpha);
    }
}
