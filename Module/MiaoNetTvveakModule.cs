using System.Reflection;
using FMOD.Studio;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MiaoNetTvveak;

public sealed class MiaoNetTvveakModule : EverestModule
{
    public static MiaoNetTvveakModule Instance { get; private set; } = null!;

    public override Type SettingsType => typeof(MiaoNetTvveakSettings);
    public static MiaoNetTvveakSettings Settings => (MiaoNetTvveakSettings)Instance._Settings;

    private readonly List<IDisposable> hooks = [];

    public MiaoNetTvveakModule()
    {
    }

    public override void Load()
    {
        Instance = this;

        Logger.Log(LogLevel.Info, "MiaoNetTvveak", "Loading features...");
        SafeLoad("GhostNameBaking", GhostNameTagBaking.Load);
    }

    public override void Unload()
    {
        for (int i = hooks.Count - 1; i >= 0; i--)
            hooks[i].Dispose();
        hooks.Clear();
    }

    public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot)
        => MiaoNetTvveakMenu.Create(menu, inGame, snapshot);

    /// <summary>安全加载一个功能模块，失败时只记录警告不影响其他功能</summary>
    private void SafeLoad(string featureName, Action<List<IDisposable>> load)
    {
        try
        {
            load(hooks);
            Logger.Log(LogLevel.Info, "MiaoNetTvveak", $"  ✓ {featureName}");
        }
        catch (Exception ex)
        {
            Logger.Log(LogLevel.Info, "MiaoNetTvveak",
                $"  ✗ {featureName} 加载失败，该功能将不可用。原因：{ex.Message}");
            Logger.LogDetailed(ex, "MiaoNetTvveak");
        }
    }
}
