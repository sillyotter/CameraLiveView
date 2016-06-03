using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace CameraLiveView.Models
{
    internal class Mp4VideoStream
    {
        private const int DefaultBufferSize = 32*1024;
        private readonly Camera _c;

        public Mp4VideoStream(string name)
        {
            // go get a camera, create only if not already set up
            _c = CameraManager.Instance.GetCamera(name);
        }

        public Task WriteToStream(Stream outputStream, HttpContent content, TransportContext context)
        {
            return Task.Run(
                async () =>
                      {
                          using (var ffmpeg = new FFMpegWrapper(_c.Frames))
                          {
                              var data = GlobalBufferManager.Instance.TakeBuffer(DefaultBufferSize);
                              try
                              {
                                  while (true)
                                  {
                                      var c = await ffmpeg.Read(data, 0, data.Length);
                                      await outputStream.WriteAsync(data, 0, c);
                                  }
                              }
                              catch (Exception e)
                              {
                                  Console.WriteLine(e);
                              }
                              finally
                              {
                                  GlobalBufferManager.Instance.ReturnBuffer(data);
                              }
                          }
                      });

        }
    }
}