using System;
using System.Net.Http;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using NLog;

namespace CameraLiveView.Models
{
    /// <summary>
    /// This is a wrapper for the camera.  There should be only one per real camera if things work right
    /// This version is a simulator, it makes its own images.  The real one woul dbe modified to fetch the values
    /// from the real physical camera with a http client.
    /// </summary>
    public class Camera
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public string Name { get; }

        private static readonly byte[] JpegHeader = {0xFF, 0xD8, 0xFF};
        private static readonly byte[] JpegFooter = {0xFF, 0xD9};
        private const int DefaultBufferSize = 32*1024; // This might need to be bigger, or perhaps smaller. depending on use
        private const double DefaultFrameLifespanSeconds = 2.0;

        private static readonly Subject<byte[]> BytesToBeReturnedToBuffer = new Subject<byte[]>();

        static Camera()
        {
            // because we cant possibly know when the frames are done, we simply wait for 2 seconds, in which time they
            // really should be done, and then we return them to the buffer.  Means we may have 2 seconds * framerate * nr of cameras
            // buffers out at any one time.  If the system is really bogged down, and it takes more than 2 seconds to send a frame out, make
            // this bigger, and figure out a way to get that going faster.  Maybe re-encode the jpegs down to something smaller, etc.
            BytesToBeReturnedToBuffer
                .Delay(TimeSpan.FromSeconds(DefaultFrameLifespanSeconds))
                .ObserveOn(TaskPoolScheduler.Default)
                .Subscribe(bytes => GlobalBufferManager.Instance.ReturnBuffer(bytes));
        }

        public Camera(string name)
        {
            Name = name;

            // http://www.insecam.org/en/bycountry/US/

            Frames = CreateMjpegFrameGrabber(
                name == "c1"
                    ? "http://50.199.22.21:84/mjpg/video.mjpg?COUNTER"
                    : "http://216.227.246.9:8083/mjpg/video.mjpg?COUNTER");
        }

        public IObservable<Tuple<byte[], int>> Frames { get; }

        private static IObservable<Tuple<byte[], int>> CreateMjpegFrameGrabber(string url)
        {
            return Observable.Create<Tuple<byte[], int>>(
                (obs, tok) =>
                {
                    return Task.Run(
                        async () =>
                              {
                                  try
                                  {
                                      using (var client = new HttpClient())
                                      using (var req = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, tok).ConfigureAwait(false))
                                      using (var s = await req.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                      {
                                          var buffer = GlobalBufferManager.Instance.TakeBuffer(DefaultBufferSize);
                                          var tempBuf = GlobalBufferManager.Instance.TakeBuffer(DefaultBufferSize);
                                          var postEndIndex = 0;
                                          var readCount = 1;
                                          var headerIndex = -1;
                                          var frameLength = 2;
                                          try
                                          {
                                              while (!tok.IsCancellationRequested && readCount > 0)
                                              {
                                                  readCount = await s.ReadAsync(tempBuf, 0, tempBuf.Length, tok).ConfigureAwait(false);
                                                  if (readCount > 0)
                                                  {
                                                      if (buffer.Length - postEndIndex < readCount)
                                                      {
                                                          var nb = GlobalBufferManager.Instance.TakeBuffer(buffer.Length*2);
                                                          Buffer.BlockCopy(buffer, 0, nb, 0, buffer.Length);
                                                          var temp = buffer;
                                                          buffer = nb;
                                                          GlobalBufferManager.Instance.ReturnBuffer(temp);
                                                      }

                                                      Buffer.BlockCopy(tempBuf, 0, buffer, postEndIndex, readCount);
                                                      postEndIndex += readCount;

                                                      if (headerIndex == -1)
                                                      {
                                                          headerIndex = buffer.Find(postEndIndex, JpegHeader, 0);
                                                      }

                                                      // im making the assumption that frames dont vary in size by that much, so rather than 
                                                      // search from headerindex on, skip forward a bit.  here, i skip forward 3/4 the previous framelength, 
                                                      // which shouldnt miss the end of the next frame, but which should reduce the amount
                                                      // of searching we do.
                                                      var footerIndex = buffer.Find(postEndIndex, JpegFooter, headerIndex + (int)(frameLength*.75));

                                                      if (headerIndex != -1 && footerIndex != -1)
                                                      {
                                                          var postFooterIndex = footerIndex + 2;
                                                          frameLength = postFooterIndex - headerIndex;

                                                          var frame = GlobalBufferManager.Instance.TakeBuffer(frameLength);
                                                          // instead of just allocating a big buffer for each frame, which may 
                                                          // in time resulting in fragmenting the LOH, we will use this buffer manager
                                                          // which keeps a pool of reusable buffers around for ever, allowing us to ignore
                                                          // fragmentation and loh GC pauses.

                                                          Buffer.BlockCopy(buffer, headerIndex, frame, 0, frameLength);

                                                          obs.OnNext(Tuple.Create(frame, frameLength));
                                                          BytesToBeReturnedToBuffer.OnNext(frame);

                                                          var tailLength = postEndIndex - postFooterIndex;
                                                          Buffer.BlockCopy(buffer, postFooterIndex, buffer, 0, tailLength);
                                                          postEndIndex = tailLength;
                                                          headerIndex = -1;
                                                      }
                                                  }
                                              }
                                          }
                                          finally
                                          {
                                              GlobalBufferManager.Instance.ReturnBuffer(buffer);
                                              GlobalBufferManager.Instance.ReturnBuffer(tempBuf);
                                          }
                                      }
                                      obs.OnCompleted();
                                  }
                                  catch (Exception e)
                                  {
                                      Log.Error(e);
                                      obs.OnError(e);
                                  }

                                  Log.Debug("Done fetching images from camera");

                                  // At some point, we will hav a network failure, and stop sending frames to the engine in here
                                  // and the outbound push contexts will stop getting data to send.  If this give sup on really long timeouts
                                  // thats good, but if it hangs, thats bad.  should be investigated.  can set timeouts in the httpclient
                                  // stream/etc above.  But, real issue is, if we stop sending frames, what does the gui on the web page do?
                                  // does it just sit there showing a static image?  does it crash and show a bad image icon?  
                                  // should we detect failure and switch to a 'bad connection' static image/frame/video/whatever?
                              }, tok);
                })
                .Publish()
                .RefCount();
        }
    }
}