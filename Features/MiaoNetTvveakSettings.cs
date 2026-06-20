namespace Celeste.Mod.MiaoNetTvveak;

public sealed class MiaoNetTvveakSettings : EverestModuleSettings
{
    public bool Enabled { get; set; } = true; // 全局开关

    public bool GhostNameBaking { get; set; } = true; // 名称预烘焙

    public bool MessageFolding { get; set; } = true; // 消息折叠
}
