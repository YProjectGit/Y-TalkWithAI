// 公式結果構造体から、教材で使う本文テキストだけを取り出す。

using System;
using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    public class OfflineRecognizerResult
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Impl
        {
            public IntPtr Text;
            public IntPtr Timestamps;
            public int Count;
            public IntPtr Tokens;
            public IntPtr Durations;
        }

        public string Text;

        public OfflineRecognizerResult(IntPtr native)
        {
            Text = string.Empty;
            if (native == IntPtr.Zero)
            {
                return;
            }

            Impl impl = (Impl)Marshal.PtrToStructure(native, typeof(Impl));
            if (impl.Text == IntPtr.Zero)
            {
                return;
            }

            Text = Marshal.PtrToStringUTF8(impl.Text);
            if (Text == null)
            {
                Text = string.Empty;
            }
        }
    }
}
