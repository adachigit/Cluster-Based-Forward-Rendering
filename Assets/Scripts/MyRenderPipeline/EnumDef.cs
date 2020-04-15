namespace MyRenderPipeline
{
    public class EnumDef
    {
        public enum LightType : uint
        {
            Directional        = 1,
            Point              = 2,
            Spot               = 3,
        }

        public enum FrustumPlaneDir : uint
        {
            Left               = 0,
            Right              = 1,
            Top                = 2,
            Bottom             = 3,
        }
    }
}
