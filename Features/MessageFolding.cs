using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Celeste.Mod.ChatInputBox;
using Celeste.Mod.MiaoNet;
using MiaoNet.Shared;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MiaoNetTvveak;

internal static class MessageFolding
{
    private sealed class FoldingInfo
    {
        public int ListIndex;
        public DateTime FirstTime;
        public DateTime LastTime;
        public int Count;
        public string DateTimeText = string.Empty;
    }

    private static readonly Dictionary<string, FoldingInfo> FoldMap = [];
    private const double FoldWindowSec = 10.0;

    private static FieldInfo? _chatViewField;
    private static FieldInfo? _chatLogField;
    private static Type? _chatItemType;
    private static PropertyInfo? _showDurationProp;

    public static void Load(List<IDisposable> hooks)
    {
        SafeAddHook(hooks, () =>
        {
            var m = typeof(ChatComponent).GetMethod(
                "Context_ChatMessageReceived",
                BindingFlags.NonPublic | BindingFlags.Instance
            )!;
            return new Hook(m,
                new Action<
                    Action<ChatComponent, OnlinePlayer?, PacketChatMessage>,
                    ChatComponent, OnlinePlayer?, PacketChatMessage
                >(OnChatMessageReceived)
            );
        });
    }

    private static void SafeAddHook(List<IDisposable> hooks, Func<IDisposable> factory)
    {
        try
        {
            hooks.Add(factory());
        }
        catch (Exception ex)
        {
            Logger.Log(LogLevel.Warn, "MiaoNetTvveak",
                $"MessageFolding: Hook 加载失败, 功能不可用. {ex.Message}");
            Logger.LogDetailed(ex, "MiaoNetTvveak");
        }
    }

    private static bool IsEnabled =>
        MiaoNetTvveakModule.Settings.Enabled && MiaoNetTvveakModule.Settings.MessageFolding;

    private static void OnChatMessageReceived(
        Action<ChatComponent, OnlinePlayer?, PacketChatMessage> orig,
        ChatComponent self, OnlinePlayer? player, PacketChatMessage packet)
    {
        if (!IsEnabled)
        {
            orig(self, player, packet);
            return;
        }

        string content = packet.Content;
        DateTime msgTime = packet.DateTime;

        ChatMessageListView chatView = GetChatView(self);
        System.Collections.IList chatLog = GetChatLog(chatView);

        if (FoldMap.TryGetValue(content, out FoldingInfo? info))
        {
            if ((msgTime - info.LastTime).TotalSeconds <= FoldWindowSec)
            {
                try
                {
                    info.Count++;
                    info.LastTime = msgTime;

                    int oldIdx = info.ListIndex;
                    chatLog.RemoveAt(oldIdx);

                    // 修正索引
                    foreach (var kvp in FoldMap)
                    {
                        if (kvp.Value.ListIndex > oldIdx)
                            kvp.Value.ListIndex--;
                    }

                    string foldedText = $"{content} ×{info.Count}";
                    var foldedChatText = new ChatText(
                        ImmutableArray.Create(new ChatTextSegment(Color.White, foldedText))
                    );

                    // 加入 chatLog（获取 ShowDuration 以重置计时器）
                    float showDuration = GetShowDuration(chatView);
                    Type itemType = GetChatItemType();
                    object newItem = Activator.CreateInstance(itemType,
                        info.DateTimeText, foldedChatText, showDuration, 1f)!;
                    chatLog.Add(newItem);

                    info.ListIndex = chatLog.Count - 1;

                    Logger.Log(LogLevel.Verbose, "MiaoNetTvveak",
                        $"MessageFolding: \"{content}\" ×{info.Count}");

                    // 不调用 orig 直接替换消息
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Log(LogLevel.Warn, "MiaoNetTvveak",
                        $"MessageFolding: 折叠时出错, 回退到正常显示. {ex.Message}");
                    info.Count--;
                    orig(self, player, packet);
                    return;
                }
            }

            // 超过 10s 另起新折叠
            orig(self, player, packet);
            info.ListIndex = chatLog.Count - 1;
            info.FirstTime = msgTime;
            info.LastTime = msgTime;
            info.Count = 1;
            info.DateTimeText = FormatTime(msgTime);
        }
        else
        {
            orig(self, player, packet);
            FoldMap[content] = new FoldingInfo
            {
                ListIndex = chatLog.Count - 1,
                FirstTime = msgTime,
                LastTime = msgTime,
                Count = 1,
                DateTimeText = FormatTime(msgTime)
            };
        }
    }

    // 反射辅助

    private static ChatMessageListView GetChatView(ChatComponent self)
    {
        _chatViewField ??= typeof(ChatComponent).GetField(
            "chatView", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (ChatMessageListView)_chatViewField.GetValue(self)!;
    }

    private static System.Collections.IList GetChatLog(ChatMessageListView self)
    {
        _chatLogField ??= typeof(ChatMessageListView).GetField(
            "chatLog", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (System.Collections.IList)_chatLogField.GetValue(self)!;
    }

    private static Type GetChatItemType()
    {
        _chatItemType ??= typeof(ChatMessageListView).GetNestedType(
            "ChatItem", BindingFlags.NonPublic)!;
        return _chatItemType;
    }

    private static float GetShowDuration(ChatMessageListView self)
    {
        _showDurationProp ??= typeof(ChatMessageListView).GetProperty(
            nameof(ChatMessageListView.ShowDuration))!;
        return (float)_showDurationProp.GetValue(self)!;
    }

    private static string FormatTime(DateTime dt)
        => dt.ToLocalTime().ToString("T", CultureInfo.InvariantCulture);
}
