using System;
using Xunit;
using CameraLiveView.Models;

namespace UnitTests
{
    public class ByteArrayExtensionTests
    {
        [Fact]
        public void FindReturnsNegOneWhenNotThere()
        {
            var data = new byte[] {0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08};
            var ans = data.Find(data.Length, new byte[] {0x09, 0x0A}, 0);
            Assert.Equal(-1, ans);
        }

        [Fact]
        public void FindReturnsIndexWhenThere()
        {
            var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
            var ans = data.Find(data.Length, new byte[] { 0x05, 0x06 }, 0);
            Assert.Equal(4, ans);
        }

        [Fact]
        public void HandlesNullSearchable()
        {
            Assert.Throws<ArgumentNullException>(() => ((byte[]) null).Find(100, new byte[] {0x01}, 0));
        }

        [Fact]
        public void HandlesNullTarget()
        {
            var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
            Assert.Throws<NullReferenceException>(() => data.Find(100, null, 0));
        }

        [Fact]
        public void HandlesNegativeStart()
        {
            var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
            Assert.Throws<ArgumentOutOfRangeException>(() => data.Find(100, null, -2));
        }

        [Fact]
        public void HandlesSearchStartGreaterThanLength()
        {
            var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
            var ans = data.Find(data.Length, new byte[] { 0x05, 0x06 }, data.Length);
            Assert.Equal(-1, ans);

            ans = data.Find(data.Length, new byte[] { 0x05, 0x06 }, data.Length+1);
            Assert.Equal(-1, ans);
        }
    }
}