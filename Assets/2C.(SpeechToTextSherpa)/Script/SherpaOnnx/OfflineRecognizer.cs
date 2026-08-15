// 公式 sherpa-onnx v1.13.5 の C# API から、offline 認識に必要な呼び出しだけ残したもの。
// ネイティブ関数名は C API と一致させる。

using System;
using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    public class OfflineRecognizer : IDisposable
    {
        HandleRef handle;

        public OfflineRecognizer(OfflineRecognizerConfig config)
        {
            IntPtr created = SherpaOnnxCreateOfflineRecognizer(ref config);
            handle = new HandleRef(this, created);
        }

        public OfflineStream CreateStream()
        {
            IntPtr stream = SherpaOnnxCreateOfflineStream(handle.Handle);
            return new OfflineStream(stream);
        }

        public void Decode(OfflineStream stream)
        {
            SherpaOnnxDecodeOfflineStream(handle.Handle, stream.Handle);
        }

        public void Dispose()
        {
            Cleanup();
            GC.SuppressFinalize(this);
        }

        ~OfflineRecognizer()
        {
            Cleanup();
        }

        void Cleanup()
        {
            if (handle.Handle != IntPtr.Zero)
            {
                SherpaOnnxDestroyOfflineRecognizer(handle.Handle);
                handle = new HandleRef(this, IntPtr.Zero);
            }
        }

        [DllImport(Dll.Filename)]
        static extern IntPtr SherpaOnnxCreateOfflineRecognizer(ref OfflineRecognizerConfig config);

        [DllImport(Dll.Filename)]
        static extern void SherpaOnnxDestroyOfflineRecognizer(IntPtr recognizer);

        [DllImport(Dll.Filename)]
        static extern IntPtr SherpaOnnxCreateOfflineStream(IntPtr recognizer);

        [DllImport(Dll.Filename)]
        static extern void SherpaOnnxDecodeOfflineStream(IntPtr recognizer, IntPtr stream);
    }
}
