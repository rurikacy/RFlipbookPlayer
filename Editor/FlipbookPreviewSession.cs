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

        internal static int Count => Sessions.Count;

        static FlipbookPreviewSessions()
        {
            AssemblyReloadEvents.beforeAssemblyReload += StopAll;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

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

        internal static void NotifyPlaybackChanged()
        {
            UpdateHookState();
        }

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

        private static void StopAll()
        {
            foreach (FlipbookPreviewSession session in Sessions.Values) session.Pause();
            EditorApplication.update -= Update;
            _updateHooked = false;
        }

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

        public FlipbookPreviewSession(Object target)
        {
            Target = target;
            InstanceId = target.GetInstanceID();
            CurrentFrame = 1;
            PreviewLoop = target is not FlipbookPlayer player || player.loop;
        }

        public Object Target { get; }
        public int InstanceId { get; }
        public int ReferenceCount { get; private set; }
        public int CurrentFrame { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool PreviewLoop { get; set; }
        public event Action Changed;

        internal void AddReference()
        {
            ReferenceCount++;
        }

        internal void RemoveReference()
        {
            ReferenceCount = Mathf.Max(0, ReferenceCount - 1);
        }

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

        public void Pause()
        {
            if (!IsPlaying) return;
            IsPlaying = false;
            NotifyChanged();
            FlipbookPreviewSessions.NotifyPlaybackChanged();
        }

        public void Stop()
        {
            IsPlaying = false;
            _elapsedTime = 0d;
            SetFrameInternal(1, true);
            FlipbookPreviewSessions.NotifyPlaybackChanged();
        }

        public void SetFrame(int frame)
        {
            int safeFrameRate = GetFrameRate();
            SetFrameInternal(frame, true);
            _elapsedTime = (CurrentFrame - 1) / (double)safeFrameRate;
            _lastUpdateTime = EditorApplication.timeSinceStartup;
        }

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

        private void NotifyChanged()
        {
            Changed?.Invoke();
        }

        private int GetTotalFrames()
        {
            if (Target is FlipbookPlayer player) return player.GetTotalFrames();
            if (Target is not FlipbookClip clip || clip.textureList == null) return 0;

            int totalFrames = 0;
            for (int i = 0; i < clip.textureList.Count; i++) totalFrames += clip.GetSafeFrameCount(i);
            return totalFrames;
        }

        private int GetFrameRate()
        {
            if (Target is FlipbookPlayer player) return Mathf.Max(1, player.frameRate);
            if (Target is FlipbookClip clip) return Mathf.Max(1, clip.frameRate);
            return 1;
        }
    }
}
