using System;
using System.Collections.Generic;

namespace FlipbookEditorTools
{
    /// <summary>
    ///     为可选集成提供不反向依赖第三方运行时类型的 Inspector 扩展点。
    /// </summary>
    public static class FlipbookEditorIntegrationRegistry
    {
        private static readonly List<Action<FlipbookPlayer>> Drawers = new();

        /// <summary>
        ///     注册一个播放器扩展 Inspector 绘制器。
        /// </summary>
        /// <param name="drawer">播放器扩展 Inspector 的绘制委托。</param>
        public static void Register(Action<FlipbookPlayer> drawer)
        {
            if (drawer != null && !Drawers.Contains(drawer)) Drawers.Add(drawer);
        }

        /// <summary>
        ///     注销一个播放器扩展 Inspector 绘制器。
        /// </summary>
        /// <param name="drawer">播放器扩展 Inspector 的绘制委托。</param>
        public static void Unregister(Action<FlipbookPlayer> drawer)
        {
            if (drawer != null) Drawers.Remove(drawer);
        }

        /// <summary>
        ///     绘制关联组件的编辑器入口。
        /// </summary>
        /// <param name="player">要操作的 Flipbook 播放器。</param>
        internal static void Draw(FlipbookPlayer player)
        {
            for (int i = 0; i < Drawers.Count; i++) Drawers[i]?.Invoke(player);
        }
    }
}
