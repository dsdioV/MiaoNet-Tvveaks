using System.Collections.Immutable;
using System.Reflection;
using Celeste.Mod.ChatInputBox;
using Celeste.Mod.MiaoNet;
using MiaoNet.Shared;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MiaoNetTvveak;

internal static class MessageFolding
{
    // ============ 折叠记忆本 ============

    private sealed class FoldingInfo
    {
        public int ListIndex;
        public DateTime FirstTime;
        public DateTime LastTime;
        public int Count;
        public OnlinePlayer? FirstPlayer;
    }

    private static readonly Dictionary<string, FoldingInfo> FoldMap = [];
    private const double FoldWindowSec = 10.0;

    // ============ 反射缓存 ============

    private static FieldInfo? _chatViewField;
    private static FieldInfo? _chatLogField;

    // ============ 加载 ============

    public static void Load(List<IDisposable> hooks)
    {
        try
        {
            var m = typeof(ChatComponent).GetMethod(
                "Context_ChatMessageReceived",
                BindingFlags.NonPublic | BindingFlags.Instance
            )!;
            hooks.Add(new Hook(m,
                new Action<
                    Action<ChatComponent, OnlinePlayer?, PacketChatMessage>,
                    ChatComponent, OnlinePlayer?, PacketChatMessage
                >(OnChatMessageReceived)
            ));
            Logger.Log(LogLevel.Info, "MiaoNetTvveak",
                "MessageFolding: Hook 已就绪。");
        }
        catch (Exception ex)
        {
            Logger.Log(LogLevel.Warn, "MiaoNetTvveak",
                $"MessageFolding: Hook 加载失败，功能不可用。{ex.Message}");
            Logger.LogDetailed(ex, "MiaoNetTvveak");
        }
    }

    private static bool IsEnabled =>
        MiaoNetTvveakModule.Settings.Enabled && MiaoNetTvveakModule.Settings.MessageFolding;

    // ============ 核心逻辑 ============

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

                    // 移除旧条目
                    int oldIdx = info.ListIndex;
                    chatLog.RemoveAt(oldIdx);

                    // 修正索引
                    foreach (var kvp in FoldMap)
                    {
                        if (kvp.Value.ListIndex > oldIdx)
                            kvp.Value.ListIndex--;
                    }

                    // 创建折叠消息（白色，不显示发送者），通过 AddLocalChat 添加
                    string foldedText = $"{content} ×{info.Count}";
                    var foldedChatText = new ChatText(
                        ImmutableArray.Create(new ChatTextSegment(Color.White, foldedText))
                    );

                    // 用反射调用 chatView.AddChatMessage(DateTime, ChatText) 添加带时间戳的消息
                    AddChatMessageWithDate(chatView, info.FirstTime, foldedChatText);

                    info.ListIndex = chatLog.Count - 1;

                    Logger.Log(LogLevel.Verbose, "MiaoNetTvveak",
                        $"MessageFolding: \"{content}\" ×{info.Count}");

                    return;
                }
                catch (Exception ex)
                {
                    Logger.Log(LogLevel.Warn, "MiaoNetTvveak",
                        $"MessageFolding: 折叠时出错，回退到正常显示。{ex.Message}");
                    // 恢复 packet 原始值
                    packet.Content = content;
                    packet.DateTime = msgTime;
                    info.Count--;
                    orig(self, player, packet);
                    return;
                }
            }

            // 超过折叠窗口，另起新折叠
            orig(self, player, packet);
            info.ListIndex = chatLog.Count - 1;
            info.FirstTime = msgTime;
            info.LastTime = msgTime;
            info.Count = 1;
            info.FirstPlayer = player;
        }
        else
        {
            // 首次出现，正常入列
            orig(self, player, packet);
            FoldMap[content] = new FoldingInfo
            {
                ListIndex = chatLog.Count - 1,
                FirstTime = msgTime,
                LastTime = msgTime,
                Count = 1,
                FirstPlayer = player
            };
        }
    }

    // ============ 反射辅助 ============

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

    private static MethodInfo? _addChatMessageMethod;

    private static void AddChatMessageWithDate(ChatMessageListView self, DateTime dateTime, ChatText chatText)
    {
        // 优先用公开的 AddChatMessage(DateTime, ChatText) 方法（wip 版有）
        _addChatMessageMethod ??= typeof(ChatMessageListView).GetMethod(
            nameof(ChatMessageListView.AddChatMessage),
            [typeof(DateTime), typeof(ChatText)]
        );

        if (_addChatMessageMethod is not null)
        {
            _addChatMessageMethod.Invoke(self, [dateTime, chatText]);
        }
        else
        {
            // alpha 版没有此重载，退回单参数版（不带时间戳）
            self.AddChatMessage(chatText);
        }
    }
}
