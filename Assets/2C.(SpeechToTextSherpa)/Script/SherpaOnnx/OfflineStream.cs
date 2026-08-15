// 公式 sherpa-onnx v1.13.5 の C# API から、波形投入と結果取得だけ残したもの。

using System;
using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    public class OfflineStream : IDisposable
    {
        HandleRef handle;

        public OfflineStream(IntPtr native)
        {
            handle = new HandleRef(this, native);
        }

        public IntPtr Handle
        {
            get { return handle.Handle; }
        }

        public void AcceptWaveform(int sampleRate, float[] samples)
        {
            SherpaOnnxAcceptWaveformOffline(handle.Handle, sampleRate, samples, samples.Length);
        }

        public OfflineRecognizerResult Result
        {
            get
            {
                IntPtr native = SherpaOnnxGetOfflineStreamResult(handle.Handle);
                OfflineRecognizerResult result = new OfflineRecognizerResult(native);
                SherpaOnnxDestroyOfflineRecognizerResult(native);
                return result;
            }
        }

        public void Dispose()
        {
            Cleanup();
            GC.SuppressFinalize(this);
        }

        ~OfflineStream()
        {
            Cleanup();
        }

        void Cleanup()
        {
            if (handle.Handle != IntPtr.Zero)
            {
                SherpaOnnxDestroyOfflineStream(handle.Handle);
                handle = new HandleRef(this, IntPtr.Zero);
            }
        }

        [DllImport(Dll.Filename)]
        static extern void SherpaOnnxAcceptWaveformOffline(
            IntPtr stream,
            int sampleRate,
            float[] samples,
            int n);

        [DllImport(Dll.Filename)]
        static extern IntPtr SherpaOnnxGetOfflineStreamResult(IntPtr stream);

        [DllImport(Dll.Filename)]
        static extern void SherpaOnnxDestroyOfflineRecognizerResult(IntPtr result);

        [DllImport(Dll.Filename)]
        static extern void SherpaOnnxDestroyOfflineStream(IntPtr stream);
    }
}
