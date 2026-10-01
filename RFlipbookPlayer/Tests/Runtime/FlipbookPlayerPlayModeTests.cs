using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RFlipbookPlayer.Tests.Runtime
{
    public sealed class FlipbookPlayerPlayModeTests
    {
        /// <summary>
        ///     验证没有 Multiple 帧时播放器不会保持播放状态。
        /// </summary>
        /// <returns>供 Unity 测试运行器执行断言的协程。</returns>
        [UnityTest]
        public IEnumerator PlayWithNoMultipleFrames_DoesNotRemainPlaying()
        {
            GameObject gameObject = new("FlipbookPlayerZeroFramePlayModeTest", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            Texture2D texture = new(2, 2);
            try
            {
                gameObject.SetActive(false);
                FlipbookPlayer player = gameObject.AddComponent<FlipbookPlayer>();
                player.autoPlayOnStart = false;
                player.frameSourceMode = FlipbookFrameSourceMode.Multiple;
                player.textureList.Add(texture);
                player.frameList.Add(0);

                gameObject.SetActive(true);
                player.Play();
                yield return null;

                Assert.That(player.IsPlaying, Is.False);
                Assert.That(player.CurrentFrameNumber, Is.EqualTo(0));
            }
            finally
            {
                Object.Destroy(gameObject);
                Object.Destroy(texture);
            }
        }
    }
}
