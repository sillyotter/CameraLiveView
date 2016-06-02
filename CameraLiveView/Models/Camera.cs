using System;
using System.Linq;
using System.Net.Http;
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
    public class Camera
    {
        public string Name { get; }

        private static int Find(byte[] data, int dl, byte[] target, int start)
        {
            if (start >= dl) return -1;
            var idx =  Array.FindIndex(data, start, b => b == target[0]);
            while (idx != -1 && idx < dl-1)
            {
                var seg = new ArraySegment<byte>(data, idx, target.Length);
                if (seg.SequenceEqual(target))
                {
                    return idx;
                }

                idx = Array.FindIndex(data, idx+1, b => b == target[0]);
            }
            return -1;
        }

        public IObservable<Tuple<byte[],int>> CreateMjpegFrameGrabber(string url)
        {
            byte[] header = {0xFF, 0xD8, 0xFF};
            byte[] footer = {0xFF, 0xD9};

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
                                        var buffer = GlobalBufferManager.Instance.TakeBuffer(32*1024);
                                        var tempBuf = GlobalBufferManager.Instance.TakeBuffer(32*1024);
                                        var postEndIndex = 0;
                                        var readCount = 1;
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
                                                      Buffer.BlockCopy(tempBuf,0,buffer,postEndIndex,readCount);
                                                      postEndIndex += readCount;
                                                      var headerIndex = Find(buffer, postEndIndex, header, 0);
                                                      var footerIndex = Find(buffer, postEndIndex, footer, headerIndex+1);
                                                      if (headerIndex != -1 && footerIndex != -1)
                                                      {
                                                          var postFooterIndex = footerIndex + 2;
                                                          var frameLength = postFooterIndex - headerIndex;

                                                          var frame = GlobalBufferManager.Instance.TakeBuffer(frameLength);
                                                          // instead of just allocating a big buffer for each frame, which will 
                                                          // in time resulting in fragmenting the LOH, we will use this buffer manager
                                                          // which keeps a pool of reusable buffers around for ever, allowing us to ignore
                                                          // fragmentation and loh GC pauses.

                                                          Buffer.BlockCopy(buffer, headerIndex, frame, 0, frameLength);
                                                          obs.OnNext(Tuple.Create(frame, frameLength));
                                                          BytesToBeReturnedToBuffer.OnNext(frame);
                                                          var tailLength = postEndIndex - postFooterIndex;
                                                          Buffer.BlockCopy(buffer, postFooterIndex, buffer, 0, tailLength);
                                                          postEndIndex = tailLength;
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
                                      Console.WriteLine(e.ToString());
                                      obs.OnError(e);
                                  }

                                  Console.WriteLine("Done fetching images from camera");

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

        
        private static readonly Subject<byte[]> BytesToBeReturnedToBuffer = new Subject<byte[]>();

        static Camera()
        {
            // because we cant possibly know when the frames are done, we simply wait for 2 seconds, in which time they
            // really should be done, and then we return them to the buffer.  Means we may have 2 seconds * framerate * nr of cameras
            // buffers out at any one time.  If the system is really bogged down, and it takes more than 2 seconds to send a frame out, make
            // this bigger, and figure out a way to get that going faster.  Maybe re-encode the jpegs down to something smaller, etc.
            BytesToBeReturnedToBuffer.Delay(TimeSpan.FromSeconds(2)).Subscribe(bytes => GlobalBufferManager.Instance.ReturnBuffer(bytes));
        }

        public Camera(string name)
        {
            Name = name;

            // tranlate name to the needed url somehow. for now im using one found on
            // http://www.insecam.org/en/bycountry/US/
            // youd want to construct it correctly with logins, settings, etc.
            // and construct the url to point to the thing indicated by name.

            //"http://50.199.22.21:84/mjpg/video.mjpg?COUNTER"


            Frames = CreateMjpegFrameGrabber(name == "c1" ? "http://50.199.22.21:84/mjpg/video.mjpg?COUNTER" : "http://216.227.246.9:8083/mjpg/video.mjpg?COUNTER");
        }

        public IObservable<Tuple<byte[], int>> Frames { get; }
    }
}