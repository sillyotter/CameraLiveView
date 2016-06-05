using System.IO;
using System.Linq;
using System.Threading;
using CameraLiveView.Models;
using Xunit;

namespace UnitTests
{
    public class JpegParserTests
    {
        private class MemoryProvider : IBufferProvider
        {
            public byte[] TakeBuffer(int size)
            {
                return new byte[size];
            }

            public void ReturnBuffer(byte[] data)
            {
            }
        }

        [Fact]
        public async void ReturnsNullWhenNoJpegFound()
        {
            using (var ms = new MemoryStream(Enumerable.Range(0, 1000).Select(x => (byte)x).ToArray()))
            using (var jr = new JpegStreamReader(ms, new MemoryProvider()))
            {
                var j = await jr.ReadJpegBytesAsync(CancellationToken.None);
                Assert.Equal(null, j);
            }
        }

        private static readonly byte[] JpegHeader = { 0xFF, 0xD8, 0xFF };
        private static readonly byte[] JpegFooter = { 0xFF, 0xD9 };

        [Fact]
        public async void CanFindOneJpeg()
        {
            var bufWithJpegInIt = 
                Enumerable.Range(0, 1000).Select(x => (byte) x)
                    .Concat(JpegHeader)
                    .Concat(Enumerable.Range(0, 1000).Select(x => (byte) x))
                    .Concat(JpegFooter)
                    .Concat(Enumerable.Range(0, 1000).Select(x => (byte) x))
                    .ToArray();

            using (var ms = new MemoryStream(bufWithJpegInIt))
            using (var jr = new JpegStreamReader(ms, new MemoryProvider()))
            {
                var j = await jr.ReadJpegBytesAsync(CancellationToken.None);
                Assert.NotEqual(null, j);
                Assert.Equal(1005, j.Item2);
                Assert.Equal(JpegHeader, j.Item1.Take(3));
                Assert.Equal(JpegFooter, j.Item1.Skip(1003).Take(2));
            }
        }

        [Fact]
        public async void CanFindTwoJpeg()
        {
            var bufWith2JpegInIt =
                Enumerable.Range(0, 1000).Select(x => (byte)x)
                    .Concat(JpegHeader)
                    .Concat(Enumerable.Repeat(0xF, 1000).Select(x => (byte)x))
                    .Concat(JpegFooter)
                    .Concat(Enumerable.Range(0, 1000).Select(x => (byte)x))
                    .Concat(JpegHeader)
                    .Concat(Enumerable.Repeat(0xA, 1000).Select(x => (byte)x))
                    .Concat(JpegFooter)
                    .Concat(Enumerable.Range(0, 1000).Select(x => (byte)x))
                    .ToArray();

            using (var ms = new MemoryStream(bufWith2JpegInIt))
            using (var jr = new JpegStreamReader(ms, new MemoryProvider()))
            {
                var j = await jr.ReadJpegBytesAsync(CancellationToken.None);
                Assert.NotEqual(null, j);
                Assert.Equal(1005, j.Item2);
                Assert.Equal(JpegHeader, j.Item1.Take(3));
                Assert.Equal(JpegFooter, j.Item1.Skip(1003).Take(2));
                Assert.True(j.Item1.Skip(3).Take(1000).All(x => x == 0xF));

                j = await jr.ReadJpegBytesAsync(CancellationToken.None);
                Assert.NotEqual(null, j);
                Assert.Equal(1005, j.Item2);
                Assert.Equal(JpegHeader, j.Item1.Take(3));
                Assert.Equal(JpegFooter, j.Item1.Skip(1003).Take(2));
                Assert.True(j.Item1.Skip(3).Take(1000).All(x => x == 0xA));
            }
        }
    }
}