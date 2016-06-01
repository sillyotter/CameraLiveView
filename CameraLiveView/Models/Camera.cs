using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;

namespace CameraLiveView.Models
{
    /// <summary>
    /// This is a wrapper for the camera.  There should be only one per real camera if things work right
    /// This version is a simulator, it makes its own images.  The real one woul dbe modified to fetch the values
    /// from the real physical camera with a http client.
    /// </summary>
    internal class Camera
    {
        public string Name { get; }

        private static int Find(List<byte> data, IReadOnlyList<byte> target, int start)
        {
            if (start >= data.Count) return -1;
            var idx = data.FindIndex(start, b => b == target[0]);
            while (idx != -1 && idx < data.Count-1)
            {
                var sub = new byte[target.Count];
                data.CopyTo(idx, sub, 0, target.Count);
                if (sub.SequenceEqual(target))
                {
                    return idx;
                }

                idx = data.FindIndex(idx+1, b => b == target[0]);
            }
            return -1;
        }

        public IObservable<byte[]> CreateMjpegFrameGrabber(string url)
        {
            byte[] header = {0xFF, 0xD8, 0xFF};
            byte[] footer = {0xFF, 0xD9};

            return Observable.Create<byte[]>(
                (obs, tok) =>
                {
                    return Task.Run(
                        async () =>
                              {
                                  // you can get rid of this stuff late
                                  // its just there to let us track the fifference
                                  // between the inbound framerate and the outbound one
                                  var sub = new Subject<int>();
                                  var disp =
                                      sub.Buffer(TimeSpan.FromSeconds(1))
                                          .ObserveOn(new EventLoopScheduler())
                                          .Subscribe(
                                              x =>
                                              {
                                                  System.Diagnostics.Debug.WriteLine($"Frames Recieved Per Seconds {x.Count}");
                                              });

                                  try
                                  {
                                      using (var client = new HttpClient())
                                      using (var req = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, tok).ConfigureAwait(false))
                                      using (var s = await req.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                      {
                                          int c = 1;
                                          var bytes = new List<byte>();
                                          while (!tok.IsCancellationRequested || c > 0)
                                          {
                                              var buf = new byte[32*1024];
                                              c = await s.ReadAsync(buf, 0, buf.Length, tok).ConfigureAwait(false);
                                              if (c > 0)
                                              {
                                                  bytes.AddRange(buf.Take(c));
                                                  var a = Find(bytes, header, 0);
                                                  var b = Find(bytes, footer, a+1);
                                                  if (a != -1 && b != -1)
                                                  {
                                                      var len = b - a + 2;
                                                      var frame = new byte[len];
                                                      bytes.CopyTo(a, frame, 0, len);
                                                      obs.OnNext(frame);
                                                      sub.OnNext(1);
                                                      bytes = new List<byte>(bytes.Skip(len));
                                                  }
                                              }
                                          }
                                      }
                                      obs.OnCompleted();
                                  }
                                  catch (Exception e)
                                  {
                                      System.Diagnostics.Debug.WriteLine(e.ToString());
                                      obs.OnError(e);
                                  }

                                  System.Diagnostics.Debug.WriteLine("Done fetching images from camera");
                                  disp.Dispose();

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

        public Camera(string name)
        {
            Name = name;

        // tranlate name to the needed url somehow. for now im using one found on
        // http://www.insecam.org/en/bycountry/US/
        // youd want to construct it correctly with logins, settings, etc.
        // and construct the url to point to the thing indicated by name.

       
            Frames = CreateMjpegFrameGrabber("http://216.227.246.9:8083/mjpg/video.mjpg?COUNTER");
        }

        public IObservable<byte[]> Frames { get; }
    }
}