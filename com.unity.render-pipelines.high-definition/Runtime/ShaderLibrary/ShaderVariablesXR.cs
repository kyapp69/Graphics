namespace UnityEngine.Rendering.HighDefinition
{
    // Custom generated by HDRP, not from Unity Engine (passed in via HDCamera)
    [GenerateHLSL(needAccessors = false, generateCBuffer = true, constantRegister = (int)ConstantRegister.XR)]
    unsafe struct ShaderVariablesXR
    {
        [HLSLArray((int)ShaderOptions.XrMaxViews, typeof(Matrix4x4))]
        public fixed float _XRViewMatrix[(int)ShaderOptions.XrMaxViews * 16];
        [HLSLArray((int)ShaderOptions.XrMaxViews, typeof(Matrix4x4))]
        public fixed float _XRInvViewMatrix[(int)ShaderOptions.XrMaxViews * 16];
        [HLSLArray((int)ShaderOptions.XrMaxViews, typeof(Matrix4x4))]
        public fixed float _XRProjMatrix[(int)ShaderOptions.XrMaxViews * 16];
        [HLSLArray((int)ShaderOptions.XrMaxViews, typeof(Matrix4x4))]
        public fixed float _XRInvProjMatrix[(int)ShaderOptions.XrMaxViews * 16];
        [HLSLArray((int)ShaderOptions.XrMaxViews, typeof(Matrix4x4))]
        public fixed float _XRViewProjMatrix[(int)ShaderOptions.XrMaxViews * 16];
        [HLSLArray((int)ShaderOptions.XrMaxViews, typeof(Matrix4x4))]
        public fixed float _XRInvViewProjMatrix[(int)ShaderOptions.XrMaxViews * 16];
        [HLSLArray((int)ShaderOptions.XrMaxViews, typeof(Matrix4x4))]
        public fixed float _XRNonJitteredViewProjMatrix[(int)ShaderOptions.XrMaxViews * 16];
        [HLSLArray((int)ShaderOptions.XrMaxViews, typeof(Matrix4x4))]
        public fixed float _XRPrevViewProjMatrix[(int)ShaderOptions.XrMaxViews * 16];
        [HLSLArray((int)ShaderOptions.XrMaxViews, typeof(Matrix4x4))]
        public fixed float _XRPrevInvViewProjMatrix[(int)ShaderOptions.XrMaxViews * 16];
        [HLSLArray((int)ShaderOptions.XrMaxViews, typeof(Matrix4x4))]
        public fixed float _XRPrevViewProjMatrixNoCameraTrans[(int)ShaderOptions.XrMaxViews * 16];
        [HLSLArray((int)ShaderOptions.XrMaxViews, typeof(Matrix4x4))]
        public fixed float _XRPixelCoordToViewDirWS[(int)ShaderOptions.XrMaxViews * 16];


        [HLSLArray((int)ShaderOptions.XrMaxViews, typeof(Vector4))]
        public fixed float _XRWorldSpaceCameraPos[(int)ShaderOptions.XrMaxViews * 4];
        [HLSLArray((int)ShaderOptions.XrMaxViews, typeof(Vector4))]
        public fixed float _XRWorldSpaceCameraPosViewOffset[(int)ShaderOptions.XrMaxViews * 4];
        [HLSLArray((int)ShaderOptions.XrMaxViews, typeof(Vector4))]
        public fixed float _XRPrevWorldSpaceCameraPos[(int)ShaderOptions.XrMaxViews * 4];
    }
}
