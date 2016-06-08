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
            // http://www.greenberrys.com/
            var url = 
            Frames = CreateMjpegFrameGrabber(
                name == "c1"
                    ? "http://50.199.22.21:84/mjpg/video.mjpg?COUNTER"
                    : "http://50.199.22.21:83/mjpg/video.mjpg?COUNTER");
        }

        public IObservable<Tuple<byte[], int>> Frames { get; }

        private static readonly byte[] JpegHeader = { 0xFF, 0xD8, 0xFF };
        private static readonly byte[] JpegFooter = { 0xFF, 0xD9 };

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
                                      /*
                                       *  
      
                                      */
                                      using (var client = new HttpClient())
                                      using (var req = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, tok).ConfigureAwait(false))
                                      using (var s = await req.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                      using (var jr = new DelimitedStreamReader(s, GlobalBufferManager.Instance, JpegHeader, JpegFooter))
                                      {
                                          var framedata = await jr.ReadJpegBytesAsync(tok);

                                          while (!tok.IsCancellationRequested && framedata != null)
                                          {
                                              var frame = framedata.Item1;

                                              obs.OnNext(framedata);
                                              BytesToBeReturnedToBuffer.OnNext(frame);

                                              framedata = await jr.ReadJpegBytesAsync(tok);
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