using System;
using System.Linq;

namespace CameraLiveView.Models
{
    public static class ByteArrayExtensions
    {
        public static int Find(this byte[] data, int dl, byte[] target, int start)
        {
            if (start >= dl) return -1;
            var idx = Array.FindIndex(data, start, b => b == target[0]);
            while (idx != -1 && idx < dl - 1)
            {
                var seg = new ArraySegment<byte>(data, idx, target.Length);
                if (seg.SequenceEqual(target))
                {
                    return idx;
                }

                idx = Array.FindIndex(data, idx + 1, b => b == target[0]);
            }
            return -1;
        }
    }
}