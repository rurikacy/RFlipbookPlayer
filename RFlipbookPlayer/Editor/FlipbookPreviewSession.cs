using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlipbookEditorTools
{
    [InitializeOnLoad]
    internal static class FlipbookPreviewSessions
    {
        private static readonly Dictionary<int, FlipbookPreviewSession> Sessions = new();
        private static bool _updateHooked;

        /// <summary>
        ///     获取当前缓存的预览会话数量。
        /// </summary>
        internal static int Count => Sessions.Count;

        /// <summary>
        ///     初始化编辑器预览会话管理器。
        /// </summary>
        static FlipbookPreviewSessions()
        {
            AssemblyReloadEvents.beforeAssemblyReload += StopAll;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        ///     获取目标对象的共享预览会话。
        /// </summary>
        /// <param name="target">要编辑或预览的播放器或 Clip。</param>
        /// <returns>目标对象的共享预览会话。</returns>
        public static FlipbookPreviewSession Acquire(Object target)
        {
            if (!target) return null;

            int instanceId = target.GetInstanceID();
            if (Sessions.TryGetValue(instanceId, out FlipbookPreviewSession session) && session.Target != target)
            {
                session.Pause();
                Sessions.Remove(instanceId);
                session = null;
            }

            if (session == null)
            {
                session = new FlipbookPreviewSession(target);
                Sessions.Add(instanceId, session);
            }

            session.AddReference();
            return session;
        }

        /// <summary>
        ///     释放目标对象的预览会话引用。
        /// </summary>
        /// <param name="session">当前编辑器预览会话。</param>
        public static void Release(FlipbookPreviewSession session)
        {
            if (session == null) return;

            session.RemoveReference();
            if (session.ReferenceCount > 0) return;

            session.Pause();
            if (Sessions.TryGetValue(session.InstanceId, out FlipbookPreviewSession current) &&
                ReferenceEquals(current, session))
                Sessions.Remove(session.InstanceId);
            UpdateHookState();
        }

        /// <summary>
        ///     通知预览管理器重新检查播放更新回调。
        /// </summary>
        internal static void NotifyPlaybackChanged()
        {
            UpdateHookState();
        }

        /// <summary>
        ///     按编辑器时间更新所有正在播放的预览会话。
        /// </summary>
        private static void Update()
        {
            double now = EditorApplication.timeSinceStartup;
            bool anyPlaying = false;
            List<int> destroyedTargets = null;

            foreach (KeyValuePair<int, FlipbookPreviewSession> pair in Sessions)
            {
                FlipbookPreviewSession session = pair.Value;
                if (!session.Target)
                {
                    destroyedTargets ??= new List<int>();
                    destroyedTargets.Add(pair.Key);
                    continue;
                }

                if (!session.IsPlaying) continue;
                session.Tick(now);
                anyPlaying |= session.IsPlaying;
            }

            if (destroyedTargets != null)
                for (int i = 0; i < destroyedTargets.Count; i++)
                    Sessions.Remove(destroyedTargets[i]);

            if (!anyPlaying) UpdateHookState();
        }

        /// <summary>
        ///     根据预览播放状态更新编辑器刷新回调。
        /// </summary>
        private static void UpdateHookState()
        {
            bool shouldHook = false;
            foreach (FlipbookPreviewSession session in Sessions.Values)
                if (session.IsPlaying)
                {
                    shouldHook = true;
                    break;
                }

            if (shouldHook == _updateHooked) return;

            if (shouldHook)
                EditorApplication.update += Update;
            else
                EditorApplication.update -= Update;

            _updateHooked = shouldHook;
        }

        /// <summary>
        ///     停止所有正在播放的编辑器预览。
        /// </summary>
        private static void StopAll()
        {
            foreach (FlipbookPreviewSession session in Sessions.Values) session.Pause();
            EditorApplication.update -= Update;
            _updateHooked = false;
        }

        /// <summary>
        ///     在编辑器播放模式切换时停止预览。
        /// </summary>
        /// <param name="state">编辑器播放模式的变化状态。</param>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state is PlayModeStateChange.ExitingEditMode or PlayModeStateChange.EnteredPlayMode)
                StopAll();
        }
    }

    internal sealed class FlipbookPreviewSession
    {
        private double _elapsedTime;
        private double _lastUpdateTime;

        /// <summary>
        ///     创建指定目标的编辑器预览会话。
        /// </summary>
        /// <param name="target">要编辑或预览的播放器或 Clip。</param>
        public FlipbookPreviewSession(Object target)
        {
            Target = target;
            InstanceId = target.GetInstanceID();
            CurrentFrame = 1;
            PreviewLoop = target is not FlipbookPlayer player || player.loop;
        }

        /// <summary>
        ///     获取当前预览的目标对象。
        /// </summary>
        public Object Target { get; }

        /// <summary>
        ///     获取预览目标的 Unity 实例标识。
        /// </summary>
        public int InstanceId { get; }

        /// <summary>
        ///     获取当前预览会话的引用数量。
        /// </summary>
        public int ReferenceCount { get; private set; }

        /// <summary>
        ///     获取当前预览的全局帧号。
        /// </summary>
        public int CurrentFrame { get; private set; }

        /// <summary>
        ///     获取预览是否正在播放。
        /// </summary>
        public bool IsPlaying { get; private set; }

        /// <summary>
        ///     获取或设置预览是否循环。
        /// </summary>
        public bool PreviewLoop { get; set; }

        public event Action Changed;

        /// <summary>
        ///     增加预览会话的引用计数。
        /// </summary>
        internal void AddReference()
        {
            ReferenceCount++;
        }

        /// <summary>
        ///     减少预览会话的引用计数。
        /// </summary>
        internal void RemoveReference()
        {
            ReferenceCount = Mathf.Max(0, ReferenceCount - 1);
        }

        /// <summary>
        ///     从当前配置的首帧开始播放。
        /// </summary>
        public void Play()
        {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;

            int totalFrames = GetTotalFrames();
            if (totalFrames <= 0) return;

            int frameRate = GetFrameRate();
            CurrentFrame = Mathf.Clamp(CurrentFrame, 1, totalFrames);
            _elapsedTime = (CurrentFrame - 1) / (double)frameRate;
            _lastUpdateTime = EditorApplication.timeSinceStartup;
            IsPlaying = true;
            NotifyChanged();
            FlipbookPreviewSessions.NotifyPlaybackChanged();
        }

        /// <summary>
        ///     暂停播放并保留当前帧。
        /// </summary>
        public void Pause()
        {
            if (!IsPlaying) return;
            IsPlaying = false;
            NotifyChanged();
            FlipbookPreviewSessions.NotifyPlaybackChanged();
        }

        /// <summary>
        ///     停止播放并回到首帧。
        /// </summary>
        public void Stop()
        {
            IsPlaying = false;
            _elapsedTime = 0d;
            SetFrameInternal(1, true);
            FlipbookPreviewSessions.NotifyPlaybackChanged();
        }

        /// <summary>
        ///     切换到指定全局帧。
        /// </summary>
        /// <param name="frame">从 1 开始的目标全局帧号。</param>
        public void SetFrame(int frame)
        {
            int safeFrameRate = GetFrameRate();
            SetFrameInternal(frame, true);
            _elapsedTime = (CurrentFrame - 1) / (double)safeFrameRate;
            _lastUpdateTime = EditorApplication.timeSinceStartup;
        }

        /// <summary>
        ///     按指定方向逐帧移动预览。
        /// </summary>
        /// <param name="direction">逐帧移动方向；正数向前，负数向后。</param>
        public void Step(int direction)
        {
            Pause();

            int totalFrames = GetTotalFrames();
            if (totalFrames <= 0) return;

            int nextFrame = CurrentFrame + direction;
            if (PreviewLoop)
                nextFrame = (nextFrame - 1 + totalFrames) % totalFrames + 1;
            else
                nextFrame = Mathf.Clamp(nextFrame, 1, totalFrames);

            SetFrame(nextFrame);
        }

        /// <summary>
        ///     按编辑器时间推进预览帧。
        /// </summary>
        /// <param name="now">当前编辑器时间，单位为秒。</param>
        internal void Tick(double now)
        {
            if (!IsPlaying || Application.isPlaying)
            {
                Pause();
                return;
            }

            int totalFrames = GetTotalFrames();
            if (totalFrames <= 0)
            {
                Pause();
                return;
            }

            double deltaTime = Mathf.Max(0f, (float)(now - _lastUpdateTime));
            _lastUpdateTime = now;
            _elapsedTime += deltaTime;

            int rawFrame = Mathf.FloorToInt((float)(_elapsedTime * GetFrameRate())) + 1;
            if (PreviewLoop)
            {
                int loopedFrame = (rawFrame - 1) % totalFrames + 1;
                SetFrameInternal(loopedFrame, false);
            }
            else if (rawFrame >= totalFrames)
            {
                SetFrameInternal(totalFrames, false);
                Pause();
            }
            else
            {
                SetFrameInternal(rawFrame, false);
            }
        }

        /// <summary>
        ///     设置预览帧并按需应用到目标。
        /// </summary>
        /// <param name="frame">从 1 开始的目标全局帧号。</param>
        /// <param name="forceApply">即使帧号未变化也重新应用预览。</param>
        private void SetFrameInternal(int frame, bool forceApply)
        {
            int totalFrames = GetTotalFrames();
            int nextFrame = totalFrames > 0 ? Mathf.Clamp(frame, 1, totalFrames) : 1;
            if (!forceApply && nextFrame == CurrentFrame) return;

            CurrentFrame = nextFrame;
            if (Target is FlipbookPlayer player && !Application.isPlaying && totalFrames > 0)
                player.PreviewFrame(CurrentFrame);

            NotifyChanged();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        /// <summary>
        ///     通知预览状态订阅者。
        /// </summary>
        private void NotifyChanged()
        {
            Changed?.Invoke();
        }

        /// <summary>
        ///     获取当前预览目标的有效总帧数。
        /// </summary>
        /// <returns>所有图集分段的有效总帧数。</returns>
        private int GetTotalFrames()
        {
            if (Target is FlipbookPlayer player) return player.GetTotalFrames();
            if (Target is not FlipbookClip clip || clip.textureList == null) return 0;

            int totalFrames = 0;
            for (int i = 0; i < clip.textureList.Count; i++) totalFrames += clip.GetSafeFrameCount(i);
            return totalFrames;
        }

        /// <summary>
        ///     获取预览目标的有效帧率。
        /// </summary>
        /// <returns>当前预览目标的有效帧率。</returns>
        private int GetFrameRate()
        {
            if (Target is FlipbookPlayer player) return Mathf.Max(1, player.frameRate);
            if (Target is FlipbookClip clip) return Mathf.Max(1, clip.frameRate);
            return 1;
        }
    }
}
