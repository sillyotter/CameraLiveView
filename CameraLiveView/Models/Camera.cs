using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using System.Web.Razor.Generator;

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

        private static int Find(byte[] data, int dl, IReadOnlyList<byte> target, int start)
        {
            if (start >= dl) return -1;
            var idx =  Array.FindIndex(data, start, b => b == target[0]);
            while (idx != -1 && idx < dl-1)
            {
                var sub = new byte[target.Count];
                Buffer.BlockCopy(data, idx, sub, 0, target.Count);
                if (sub.SequenceEqual(target))
                {
                    return idx;
                }

                idx = Array.FindIndex(data, idx+1, b => b == target[0]);
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
                                          // switching this to use buffers should remove some pressure on the 
                                          // garbage collector.  the othe version worked, but this should be incrementally
                                          // faster, and reduce the amount of GC pausing we run into.
                                          var buffer = new byte[32*1024];
                                          var tempBuf = new byte[32 * 1024];
                                          var postEndIndex = 0;
                                          var readCount = 1;

                                          while (!tok.IsCancellationRequested && readCount > 0)
                                          {
                                              readCount = await s.ReadAsync(tempBuf, 0, tempBuf.Length, tok).ConfigureAwait(false);
                                              if (readCount > 0)
                                              {
                                                  if (buffer.Length - postEndIndex < readCount)
                                                  {
                                                      Array.Resize(ref buffer, buffer.Length * 2);
                                                  }
                                                  Buffer.BlockCopy(tempBuf,0,buffer,postEndIndex,readCount);
                                                  postEndIndex += readCount;
                                                  var headerIndex = Find(buffer, postEndIndex, header, 0);
                                                  var footerIndex = Find(buffer, postEndIndex, footer, headerIndex+1);
                                                  if (headerIndex != -1 && footerIndex != -1)
                                                  {
                                                      var postFooterIndex = footerIndex + 2;
                                                      var frameLength = postFooterIndex - headerIndex;

                                                      var frame = new byte[frameLength];
                                                      // we could potentially use some kind of pool of frame buffers here
                                                      // to keep from allocating them all the time.  Not sure how to know they are done
                                                      // and return them. BufferManager class might handle some of it, but still
                                                      // need to track being done.  When we have N subscribers, each gets one, need
                                                      // somehow to know how many so we can tell when they are all done
                                                      // also in some cases, te frame senders will skip frames, how to tell those im done
                                                      // with them?  For now, just allocate them?

                                                      // I can be fairly sure they are done after N seconds has passed.  the most recent frames
                                                      // get sent, old ones are dropped, it doesnt take that long for all of the sender threads
                                                      // to send the most recent one, so what if I just used a buffer manager, created the buffers here
                                                      // and somehow scheduled the thing to be returned in 5 seconds?  2?  would decrese LOH 
                                                      // allocations and fragmentations a good deal, would be rather large buffer though, with 50 frames 
                                                      // worth of data sitting around;

                                                      Buffer.BlockCopy(buffer, headerIndex, frame, 0, frameLength);
                                                      obs.OnNext(frame);
                                                      sub.OnNext(1);
                                                      var tailLength = postEndIndex - postFooterIndex;
                                                      Buffer.BlockCopy(buffer, postFooterIndex, buffer, 0, tailLength);
                                                      postEndIndex = tailLength;
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