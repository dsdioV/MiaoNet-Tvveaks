namespace Celeste.Mod.MiaoNetTvveak;

/// <summary>
/// MiaoNetTvveak 全局设置。
/// 每个功能有独立开关，"启用所有 Tweaks"关闭后全部透传。
/// 本地化键名: modoptions_miaonettvveaksettings_{属性名}
/// </summary>
public sealed class MiaoNetTvveakSettings : EverestModuleSettings
{
    public bool Enabled { get; set; } = true;

    public bool GhostNameBaking { get; set; } = true;
}
