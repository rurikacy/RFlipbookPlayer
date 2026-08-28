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
