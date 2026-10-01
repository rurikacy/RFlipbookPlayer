using System.Collections.Generic;
using System.Reflection;
using FlipbookEditorTools;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RFlipbookPlayer.Tests.Editor
{
    public sealed class FlipbookPlayerTests
    {
        /// <summary>
        ///     确认四边按纹理像素内收，且过大的设置不会使帧 UV 翻转。
        /// </summary>
        [Test]
        public void InsetFrameRect_UsesTexturePixelsAndKeepsPositiveArea()
        {
            Texture2D texture = new(100, 80);
            try
            {
                Rect frame = new(0.2f, 0.25f, 0.4f, 0.5f);
                Rect inset = FlipbookPlayer.InsetFrameRect(frame, texture, 1, 2, 3, 4);

                Assert.That(inset.xMin, Is.EqualTo(0.23f).Within(0.0001f));
                Assert.That(inset.xMax, Is.EqualTo(0.56f).Within(0.0001f));
                Assert.That(inset.yMin, Is.EqualTo(0.275f).Within(0.0001f));
                Assert.That(inset.yMax, Is.EqualTo(0.7375f).Within(0.0001f));

                Rect excessive = FlipbookPlayer.InsetFrameRect(frame, texture, 100, 100, 100, 100);
                Assert.That(excessive.width, Is.EqualTo(1f / texture.width).Within(0.0001f));
                Assert.That(excessive.height, Is.EqualTo(1f / texture.height).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        ///     验证同一帧上的所有事件均会触发。
        /// </summary>
        [Test]
        public void DispatchFrameEvents_InvokesEveryEventOnSameFrame()
        {
            GameObject gameObject = new("FlipbookPlayerEventProxyTest");
            try
            {
                gameObject.AddComponent<FlipbookPlayer>();
                FlipbookPlayerEventProxy proxy = gameObject.AddComponent<FlipbookPlayerEventProxy>();
                int unityEventCount = 0;
                int csharpEventCount = 0;
                List<FlipbookPlayerEventProxy.FrameEvent> events = new();

                for (int i = 0; i < 2; i++)
                {
                    UnityEvent onReached = new();
                    onReached.AddListener(() => unityEventCount++);
                    events.Add(new FlipbookPlayerEventProxy.FrameEvent
                    {
                        frameNumber = 1,
                        onReached = onReached
                    });
                }

                typeof(FlipbookPlayerEventProxy)
                    .GetField("frameEvents", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(proxy, events);
                proxy.FrameReached += _ => csharpEventCount++;

                typeof(FlipbookPlayerEventProxy)
                    .GetMethod("DispatchFrameEvents", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(proxy, new object[] { 0, 1 });

                Assert.That(unityEventCount, Is.EqualTo(2));
                Assert.That(csharpEventCount, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        /// <summary>
        ///     验证编辑模式停用播放器后恢复 RawImage 原纹理。
        /// </summary>
        [Test]
        public void DisableInEditMode_RestoresRawImageTexture()
        {
            GameObject gameObject = new("FlipbookRawImageRestoreTest", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            Texture2D originalTexture = new(2, 2);
            Texture2D flipbookTexture = new(2, 2);
            try
            {
                gameObject.SetActive(false);
                RawImage rawImage = gameObject.GetComponent<RawImage>();
                rawImage.texture = originalTexture;

                FlipbookPlayer player = gameObject.AddComponent<FlipbookPlayer>();
                player.row = 1;
                player.column = 1;
                player.textureList.Add(flipbookTexture);
                player.frameList.Add(1);

                gameObject.SetActive(true);
                player.RefreshPreview();
                Assert.That(rawImage.texture, Is.SameAs(flipbookTexture));

                gameObject.SetActive(false);
                Assert.That(rawImage.texture, Is.SameAs(originalTexture));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(originalTexture);
                Object.DestroyImmediate(flipbookTexture);
            }
        }

        /// <summary>
        ///     验证销毁目标后的预览会话可以释放。
        /// </summary>
        [Test]
        public void ReleaseDestroyedTarget_RemovesPreviewSession()
        {
            int initialSessionCount = FlipbookPreviewSessions.Count;
            FlipbookClip clip = ScriptableObject.CreateInstance<FlipbookClip>();
            FlipbookPreviewSession session = FlipbookPreviewSessions.Acquire(clip);
            int instanceId = clip.GetInstanceID();

            Object.DestroyImmediate(clip);
            Assert.That(session.InstanceId, Is.EqualTo(instanceId));

            FlipbookPreviewSessions.Release(session);
            Assert.That(FlipbookPreviewSessions.Count, Is.EqualTo(initialSessionCount));
        }

        /// <summary>
        ///     验证释放旧会话时不会误删替代会话。
        /// </summary>
        [Test]
        public void ReleaseStaleSession_DoesNotRemoveReplacement()
        {
            FlipbookClip staleClip = ScriptableObject.CreateInstance<FlipbookClip>();
            FlipbookClip replacementClip = ScriptableObject.CreateInstance<FlipbookClip>();
            FlipbookPreviewSession stale = FlipbookPreviewSessions.Acquire(staleClip);
            FlipbookPreviewSession replacement = FlipbookPreviewSessions.Acquire(replacementClip);
            Dictionary<int, FlipbookPreviewSession> sessions =
                (Dictionary<int, FlipbookPreviewSession>)typeof(FlipbookPreviewSessions)
                    .GetField("Sessions", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.GetValue(null);

            try
            {
                Assert.That(sessions, Is.Not.Null);
                sessions[stale.InstanceId] = replacement;

                FlipbookPreviewSessions.Release(stale);

                Assert.That(sessions.TryGetValue(stale.InstanceId, out FlipbookPreviewSession current), Is.True);
                Assert.That(current, Is.SameAs(replacement));
            }
            finally
            {
                sessions?.Remove(stale.InstanceId);
                FlipbookPreviewSessions.Release(replacement);
                Object.DestroyImmediate(staleClip);
                Object.DestroyImmediate(replacementClip);
            }
        }
    }
}
