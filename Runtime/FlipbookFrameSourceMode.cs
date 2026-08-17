/// <summary>
///     Flipbook 帧的识别方式。
/// </summary>
public enum FlipbookFrameSourceMode
{
    /// <summary>
    ///     使用固定行列网格计算帧 UV。
    /// </summary>
    Grid,

    /// <summary>
    ///     使用 Texture 的 Multiple Sprite 切片 UV。
    /// </summary>
    Multiple
}