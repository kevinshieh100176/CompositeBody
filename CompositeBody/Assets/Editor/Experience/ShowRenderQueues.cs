namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Where this show's own transparent materials sit in the render queue, and why it is not
    /// just RenderQueue.Transparent.
    ///
    /// THE ROOM IS STANDING IN WATER. There is a KWS WaterSystem under the whole play space --
    /// Experience/Beats/--- Environment ---/Water System, its surface 19 cm off the floor --
    /// and KWS does not draw at one queue, it draws at three:
    ///
    ///   water surface   DefaultWaterQueue + TransparentSortingPriority   = 3000 - 1 = 2999
    ///   shoreline foam  the same + ShorelineQueueOffset  (+1)            = 3000
    ///   underwater      the same + UnderwaterQueueOffset (+1)            = 3000
    ///
    /// So two of its three passes land on EXACTLY RenderQueue.Transparent, which is where every
    /// transparent material in Unity starts. Inside one queue, transparent geometry is sorted
    /// back to front by distance -- and a water plane that spans the room resolves to a
    /// distance that can come out in front of a small prop, so the foam draws over the top of
    /// it. What that looks like is a lamp, a jacket or a footprint that is simply not there,
    /// from some angles and not others, with nothing in the hierarchy or the material to
    /// suggest why.
    ///
    /// 3100 CLEARS ALL OF IT, with room to spare: TransparentSortingPriority is a slider from
    /// -50 to 50, so the highest KWS can reach is 3000 + 50 + 1 = 3051 even if someone pushes
    /// it all the way up. It is still inside the transparent range (3000-3500), so these
    /// materials still sort back to front against each other, which is what they need.
    ///
    /// WHAT DOES NOT NEED THIS. Anything opaque or alpha-clipped is already finished before the
    /// water starts: the cartons and the wall of boxes are at 2450 (AlphaTest) and the 真人
    /// point clouds at 2000 (Geometry). This is only for materials that actually blend.
    /// </summary>
    public static class ShowRenderQueues
    {
        /// <summary>Transparent, but above every pass the water system occupies.</summary>
        public const int AboveWater = 3100;
    }
}
