namespace CameraLiveView.Models
{
    public interface IBufferProvider
    {
        byte[] TakeBuffer(int size);
        void ReturnBuffer(byte[] data);
    }
}