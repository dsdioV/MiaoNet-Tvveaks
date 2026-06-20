using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste.Mod.MiaoNet;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MiaoNetTvveak;
internal static class GhostNameTagBaking
{
    private sealed class BakeData : IDisposable
    {
        public int PlayerID;
        public bool ShowAvatar;
        public string? DisplayText; // 不含头像的纯文本
        public VirtualRenderTarget? BakedTexture;
        public VirtualRenderTarget? BakedAvatar;
        public bool TextureBaked;
        public bool AvatarBaked;

        public void Dispose()
        {
            BakedTexture?.Dispose();
            BakedTexture = null;
            BakedAvatar?.Dispose();
            BakedAvatar = null;
        }
    }

    private static readonly ConditionalWeakTable<GhostNameTag, BakeData> _bakeDataMap = new();

    private const float Scale = 1f / 2f;
    private const float Stroke = 2f;
    private const float ExtraMargin = 2f;
    private const float AvatarGap = 4f;
    private const float Margin = 8f;

    public static void Load(List<IDisposable> hooks)
    {
        SafeAddHook(hooks, () =>
        {
            var m = typeof(GhostNameTag).GetConstructor(
                BindingFlags.Public | BindingFlags.Instance,
                null, [typeof(Player), typeof(OnlinePlayer), typeof(bool)], null
            )!;
            return new Hook(m, new Action<Action<GhostNameTag, Player, OnlinePlayer, bool>, GhostNameTag, Player, OnlinePlayer, bool>(OnCtorPlayer));
        });

        SafeAddHook(hooks, () =>
        {
            var m = typeof(GhostNameTag).GetConstructor(
                BindingFlags.Public | BindingFlags.Instance,
                null, [typeof(MiaoNetGhost), typeof(OnlinePlayer), typeof(bool)], null
            )!;
            return new Hook(m, new Action<Action<GhostNameTag, MiaoNetGhost, OnlinePlayer, bool>, GhostNameTag, MiaoNetGhost, OnlinePlayer, bool>(OnCtorGhost));
        });

        SafeAddHook(hooks, () =>
        {
            var m = typeof(Entity).GetMethod(
                nameof(Entity.Update),
                BindingFlags.Public | BindingFlags.Instance,
                null, Type.EmptyTypes, null
            )!;
            return new Hook(m, new Action<Action<Entity>, Entity>(OnEntityUpdate));
        });

        SafeAddHook(hooks, () =>
        {
            var m = typeof(GhostNameTag).GetMethod(
                nameof(GhostNameTag.Render),
                BindingFlags.Public | BindingFlags.Instance,
                null, Type.EmptyTypes, null
            )!;
            return new Hook(m, new Action<Action<GhostNameTag>, GhostNameTag>(OnRender));
        });

        SafeAddHook(hooks, () =>
        {
            var m = typeof(Entity).GetMethod(
                nameof(Entity.Removed),
                BindingFlags.Public | BindingFlags.Instance,
                null, [typeof(Scene)], null
            )!;
            return new Hook(m, new Action<Action<Entity, Scene>, Entity, Scene>(OnEntityRemoved));
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
            Logger.Log(LogLevel.Info, "MiaoNetTvveak",
                $"GhostNameTagBaking: 某个 Hook 加载失败, 部分功能可能不可用{ex.Message}");
        }
    }

    private static bool IsEnabled =>
        MiaoNetTvveakModule.Settings.Enabled && MiaoNetTvveakModule.Settings.GhostNameBaking;

    private static void OnCtorPlayer(
        Action<GhostNameTag, Player, OnlinePlayer, bool> orig,
        GhostNameTag self, Player player, OnlinePlayer onlinePlayer, bool avatar)
    {
        orig(self, player, onlinePlayer, avatar);
        if (IsEnabled)
            _bakeDataMap.Add(self, new BakeData { PlayerID = onlinePlayer.ID, ShowAvatar = avatar });
    }

    private static void OnCtorGhost(
        Action<GhostNameTag, MiaoNetGhost, OnlinePlayer, bool> orig,
        GhostNameTag self, MiaoNetGhost ghost, OnlinePlayer onlinePlayer, bool avatar)
    {
        orig(self, ghost, onlinePlayer, avatar);
        if (IsEnabled)
            _bakeDataMap.Add(self, new BakeData { PlayerID = onlinePlayer.ID, ShowAvatar = avatar });
    }

    private static void OnEntityUpdate(Action<Entity> orig, Entity self)
    {
        orig(self);

        if (!IsEnabled)
            return;

        if (self is GhostNameTag tag && _bakeDataMap.TryGetValue(tag, out var data))
        {
            if (!data.TextureBaked)
                BakeTexture(tag, data);
            if (data.ShowAvatar && !data.AvatarBaked && AvatarReady(data))
                BakeAvatar(data);
        }
    }

    private static void OnEntityRemoved(Action<Entity, Scene> orig, Entity self, Scene scene)
    {
        orig(self, scene);

        if (!IsEnabled)
            return;

        if (self is GhostNameTag tag && _bakeDataMap.TryGetValue(tag, out var data))
        {
            data.Dispose();
            _bakeDataMap.Remove(tag);
        }
    }

    private static void OnRender(Action<GhostNameTag> orig, GhostNameTag self)
    {
        if (!IsEnabled)
        {
            orig(self);
            return;
        }

        if (!_bakeDataMap.TryGetValue(self, out var data))
        {
            orig(self);
            return;
        }

        if (self.Scene is not Level level)
        {
            orig(self);
            return;
        }

        if (!data.TextureBaked || data.BakedTexture is null)
        {
            orig(self);
            return;
        }

        RenderBaked(self, level, data);
    }

    private static void BakeTexture(GhostNameTag self, BakeData data)
    {
        data.DisplayText = StripAvatarEmoji(self.Text);
        Vector2 textSize = MiaoNetFont.Measure(data.DisplayText) * Scale;
        int w = Math.Max(1, (int)(textSize.X + (Stroke + ExtraMargin) * 2));
        int h = Math.Max(1, (int)(textSize.Y + (Stroke + ExtraMargin) * 2));

        var rt = VirtualContent.CreateRenderTarget("miaonet-name", w, h, false, true, 0);
        data.BakedTexture = rt;
        data.TextureBaked = true;

        BakeToRT(rt, data.DisplayText, self.Color, w, h);
    }

    private static void BakeAvatar(BakeData data)
    {
        string avtText = $":\0mn_avt_{data.PlayerID}:";
        float avtSize = 64f * Scale;
        int w = Math.Max(1, (int)(avtSize + (Stroke + ExtraMargin) * 2));
        int h = Math.Max(1, (int)(avtSize + (Stroke + ExtraMargin) * 2));

        var rt = VirtualContent.CreateRenderTarget("miaonet-avt", w, h, false, true, 0);
        data.BakedAvatar = rt;
        data.AvatarBaked = true;

        BakeToRT(rt, avtText, Color.White, w, h);
    }

    private static void BakeToRT(VirtualRenderTarget rt, string text, Color color, int w, int h)
    {
        var gd = Engine.Instance.GraphicsDevice;
        var prev = gd.GetRenderTargets();
        gd.SetRenderTarget(rt);
        gd.Clear(Color.Transparent);

        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone,
            null,
            Matrix.Identity
        );

        Vector2 pad = new(Stroke + ExtraMargin, Stroke + ExtraMargin);
        MiaoNetFont.ENZhsFont.DrawOutline(
            MiaoNetFont.ENZhsBaseSize, text,
            pad, Vector2.Zero, Vector2.One * Scale,
            color, Stroke, Color.Black
        );

        Draw.SpriteBatch.End();

        if (prev is { Length: > 0 })
            gd.SetRenderTargets(prev);
        else
            gd.SetRenderTarget(null);
    }

    private static bool AvatarReady(BakeData data)
    {
        try
        {
            string avtText = $":\0mn_avt_{data.PlayerID}:";
            string applied = Emoji.Apply(avtText);
            return applied.Length > 0 && applied[0] != ':';
        }
        catch
        {
            return false;
        }
    }

    private static string StripAvatarEmoji(string text)
    {
        int nullIdx = text.IndexOf('\0');
        if (nullIdx >= 0)
        {
            int secondColon = text.IndexOf(':', nullIdx + 1);
            if (secondColon > nullIdx)
                return text[(secondColon + 1)..].TrimStart();
        }
        return text;
    }

    private static void RenderBaked(GhostNameTag self, Level level, BakeData data)
    {
        Vector2 worldPosition = self.Entity.Position;
        worldPosition.Y -= 16f;

        Vector2 position = level.WorldToScreen(worldPosition);
        Vector2 textSize = MiaoNetFont.Measure(data.DisplayText ?? self.Text) * Scale;

        Vector2 clampedPosition = ScreenClamper.ClampIntoScreen(
            position, textSize,
            new Vector2(0.5f, 1f), Margin
        );

        var settings = MiaoNetModule.Settings;
        float alpha = self.IsOnSelf
            ? settings.SelfNameOpacityValue
            : settings.PlayerNameOpacityValue;

        if (alpha <= 0f || data.BakedTexture == null)
            return;

        // 补偿烘焙时文字右下偏移
        Vector2 compensation = new(Stroke + ExtraMargin, Stroke + ExtraMargin);
        Vector2 namePos = clampedPosition - new Vector2(textSize.X / 2f, textSize.Y) - compensation;

        if (data.ShowAvatar)
        {
            float avtW = 64f * Scale;
            Vector2 avtPos = namePos - new Vector2(avtW + AvatarGap * Scale, 0f);
            Vector2 avtCompensation = new(Stroke + ExtraMargin, Stroke + ExtraMargin);

            if (data.AvatarBaked && data.BakedAvatar != null)
            {
                Draw.SpriteBatch.Draw(data.BakedAvatar, avtPos - avtCompensation, Color.White * alpha);
            }
            else
            {
                MTexture fallback = GFX.Gui["miaonet/missing_avatar"];
                float s = avtW / fallback.Width;
                fallback.Draw(avtPos, Vector2.Zero, Color.White * alpha, Vector2.One * s);
            }
        }

        // 绘制烘焙好的名字纹理
        Draw.SpriteBatch.Draw(data.BakedTexture, namePos, Color.White * alpha);
    }

}
