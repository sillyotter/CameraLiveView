using System;
using System.ServiceModel.Channels;

namespace CameraLiveView.Models
{
    public class GlobalBufferManager : IBufferProvider
    {
        private const int TotalBufferMemory = 100*1024*1024;
        private const int MaxBufferSize = 128*1024;

        private GlobalBufferManager()
        {
        }

        private static readonly Lazy<GlobalBufferManager> SingletonInstance = new Lazy<GlobalBufferManager>(() => new GlobalBufferManager());

        public static GlobalBufferManager Instance => SingletonInstance.Value;

        // I really dont know the right values for these buffers.  It largely depends on the installation
        // The max size will depend on the frame size and jpeg compression levels, the delay below, and the number of max
        // active cameras.  You'll need to work out the right sizes here, but if we say 64K per frame, 20 cameras, 30fps,  and a 2 second delay
        // before were sure were done with the frame, we need 77MB.  Add in some overhead for other things, and lets call it 100MB.  thats not huge
        // and your machine probably has that to spare.  The benefit though, is we never have toworry about the LOH going nuts and slowing us down,
        // which is a real issue in something like this.  The max requseted size of 128K comes from the notion that the jpeg frame grabber so far has never needed
        // more than 64K, so I doubled that, to be safe. 
        // Im not sure if this is per app domain or total, but im assuming per app domain. 
        // this could be made smaller.  if you know the nr of cameras is less, or that the frame size is smaller, or you are certain you can manage with a 
        // shorter frame buffer reclimation delay.  Or maybe you need it bigger.  
        private readonly BufferManager _bm  = BufferManager.CreateBufferManager(TotalBufferMemory, MaxBufferSize);

        public byte[] TakeBuffer(int size)
        {
            return _bm.TakeBuffer(size);
        }

        public void ReturnBuffer(byte[] buf)
        {
            _bm.ReturnBuffer(buf);
        }
    }
}